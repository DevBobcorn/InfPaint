import json

with open("config.json") as f:
    conf = json.load(f)

import requests
import io
import base64
from PIL import Image, ImageFilter

pos_prompt = conf['inpaint_pos_prompt']
neg_prompt = conf['inpaint_neg_prompt']
api = conf['inpaint_api']

def img2img(image_path, mask_path, seed, output_path):
    api_url = f"{api}/sdapi/v1/img2img"

    with open(image_path, 'rb') as file:
        image_data = file.read()
        image_pil = Image.open(file)

        longer_side = int(conf['inpaint_resize_longer_side'])

        # Scale down
        if image_pil.width >= image_pil.height:
            image_w = longer_side
            image_h = round(image_pil.height * (longer_side / image_pil.width))
        else:
            image_w = round(image_pil.width * (longer_side / image_pil.height))
            image_h = longer_side
        
        print(f'Scaling to ({image_w}, {image_h})')
        image_pil = image_pil.resize((image_w, image_h), Image.Resampling.LANCZOS).convert('RGB')
        # Assign back to file binary
        img_byte_arr = io.BytesIO()
        image_pil.save(img_byte_arr, format='JPEG')
        image_data = img_byte_arr.getvalue()
    
    with open(mask_path, 'rb') as file:
        mask_data = file.read()
        mask_pil = Image.open(file)
        # Scale down
        mask_pil = mask_pil.resize((image_w, image_h), Image.Resampling.LANCZOS)

        # Dilate and blur the mask
        mask_pil = mask_pil.filter(ImageFilter.MaxFilter(7))
        mask_pil = mask_pil.filter(ImageFilter.GaussianBlur(3))

        # Assign back to file binary
        img_byte_arr = io.BytesIO()
        mask_pil.save(img_byte_arr, format='PNG')
        mask_data = img_byte_arr.getvalue()
    
    encoded_image = base64.b64encode(image_data).decode('utf-8')
    encoded_mask  = base64.b64encode(mask_data).decode('utf-8')

    payload = {
        "alwayson_scripts": {
            "ControlNet": {
                "args": [
                    {
                        "enabled": True,

                        "module": "dw_openpose_full",
                        "model": "control_v11p_sd15_openpose [cab727d4]",

                        "control_mode": "Balanced",
                        "guidance_end": 1.0,
                        "guidance_start": 0.0,
                        "hr_option": "Both",
                        "inpaint_crop_input_image": True,
                        "input_mode": "simple",
                        "is_ui": False,
                        "loopback": False,
                        "low_vram": False,
                        
                        "processor_res": 512,
                        "pulid_mode": "Fidelity",
                        "resize_mode": "Crop and Resize",
                        "save_detected_map": False,
                        "threshold_a": 0.5,
                        "threshold_b": 0.5,
                        "union_control_type": "OpenPose",
                        "weight": 1.0
                    },
                    {
                        "enabled": True,

                        "module": "inpaint_only", # inpaint_global_harmonious
                        "model": "control_v11p_sd15_inpaint [ebff9138]",
                        #"image": encoded_mask,

                        "processor_res": 512,

                        "control_mode": "Balanced",
                        "guidance_end": 1.0,
                        "guidance_start": 0.0,
                        "hr_option": "Both",
                        "inpaint_crop_input_image": False,
                        "input_mode": "simple",
                        "is_ui": False,
                        "loopback": False,
                        "low_vram": False,
                        
                        "resize_mode": "Crop and Resize",
                        "save_detected_map": False,
                        "threshold_a": 0.5,
                        "threshold_b": 0.5,
                        "weight": 1.0
                    }
                ]
            },
            "Sampler": {
                "args": [
                    30,
                    "Euler a",
                    "Automatic"
                ]
            },
            "Seed": {
                "args": [
                    -1,
                    False,
                    -1,
                    0,
                    0,
                    0
                ]
            },
            "Soft Inpainting": {
                "args": [
                    True,
                    1,
                    0.5,
                    4,
                    0,
                    0.5,
                    2
                ]
            }
        },
        "init_images": [encoded_image],
        "mask": encoded_mask,
        "mask_blur": 4, # Set this to higher value if Soft Inpainting is enabled
        "mask_blur_x": 4,
        "mask_blur_y": 4,
        "mask_round": False,

        "seed": seed,
        "seed_enable_extras": True,
        "seed_resize_from_h": -1,
        "seed_resize_from_w": -1,

        "steps": 30,
        "scale_by": 0.5,
        "width": image_w,
        "height": image_h,
        "prompt": pos_prompt,
        "negative_prompt": neg_prompt,
        "batch_size": 1,
        "cfg_scale": 7,
        "denoising_strength": 0.75,
        "image_cfg_scale": 1.5,
        "initial_noise_multiplier": 1,
        "inpaint_full_res": 0,
        "inpaint_full_res_padding": 32,
        "inpainting_fill": 1, # 0 for fill, 1 for original content
        "inpainting_mask_invert": 0,

        "n_iter": 1,
        "resize_mode": 0,
        "restore_faces": False,
        "s_churn": 0,
        "s_min_uncond": 0,
        "s_noise": 1,
        "s_tmax": 1,
        "s_tmin": 0,
        "sampler_name": "Euler a",
        "scheduler": "Automatic",
    }

    response = requests.post(api_url, json=payload)
    
    if response.status_code == 200:
        response_data = response.json()
        encoded_result = response_data["images"][0]
        result_data = base64.b64decode(encoded_result)

        with open(output_path, 'wb') as file:
            file.write(result_data)
    else:
        print("Unexpected error occurred:", response.text)

import glob, os

handled_extn_names = conf['accept_types']
dir_i = conf['inpaint_dir_i']
dir_o = conf['inpaint_dir_o']

if not os.path.exists(dir_o):
    os.mkdir(dir_o)

for file in glob.glob(f'{dir_i}/*'):
    file = file.replace('\\', '/')
    file_name = file.rsplit('/', 1)[1]
    extn_index = file_name.rfind('.')

    base_name = file_name[:extn_index].lower()
    extn_name = file_name[extn_index:].lower()

    seeds = [42, 1337, 2077]
    output_name_format = conf['inpaint_name_format']

    if not base_name.endswith('_mask') and extn_name in handled_extn_names:
        print(f'Processing [{base_name}][{extn_name}]...')
        source_path = f'{dir_i}/{base_name}{extn_name}'
        mask_path   = f'{dir_i}/{base_name}_mask.png'
        
        if os.path.isfile(mask_path):
            for seed in seeds:
                output_path = f'{dir_o}/{output_name_format.format(base_name, seed)}'
                img2img(source_path, mask_path, seed, output_path)
        else:
            print(f'Mask for {source_path} is not present. Skipped.')
