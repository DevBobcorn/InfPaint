import json

with open("config.json") as f:
    conf = json.load(f)
    
import torch
from torchvision.ops import box_convert
from local_groundingdino.util.inference import load_model as load_dino_model, load_image, predict, annotate
from sam2.build_sam import build_sam2
from sam2.sam2_image_predictor import SAM2ImagePredictor
from scipy.ndimage import binary_dilation
from PIL import Image
import matplotlib.pyplot as plt
import numpy as np
import cv2
import random

# Disable Torch warnings
import warnings
torch.set_warn_always(False)
warnings.filterwarnings("ignore", category=FutureWarning)
warnings.filterwarnings("ignore", category=UserWarning)

device = conf['mask_gen_device']

# Load the Grounding DINO model
print('Loading Grouding DINO...')
dino_model = load_dino_model(conf['dino_config'], conf['dino_model'])

# Load the SAM2 model
print('Loading SAM2...')
sam_predictor = SAM2ImagePredictor(build_sam2(conf['sam_config'], conf['sam_model']))

def check_box_text_prompt(box_prompts: str, check_prompts: list[str]) -> bool:
    for prompt_name in box_prompts.split(' '):
        if prompt_name in check_prompts:
            return True
    
    return False

def combine_masks_as_ndarray(mask_ndarray: np.ndarray) -> np.ndarray:
    # mask_ndarray: BxCxHxW, B is count of separate masks predicted by SAM
    if len(mask_ndarray.shape) == 3:
        mask_ndarray = np.expand_dims(mask_ndarray, axis=0)

    B, C, H, W = mask_ndarray.shape
    combined_masks = np.zeros((H, W))

    for b in range(B):
        for c in range(C):
            combined_masks = np.maximum(combined_masks, mask_ndarray[b, c])
    
    #plt.imshow(combined_masks)
    #plt.show()

    return combined_masks

def dilate_mask(mask, dilation) -> np.ndarray:
    x, y = np.meshgrid(np.arange(dilation), np.arange(dilation))
    center = dilation // 2
    dilation_kernel = ((x - center)**2 + (y - center)**2 <= center**2).astype(np.uint8)
    dilated_mask = binary_dilation(mask, dilation_kernel)

    return dilated_mask

text_prompt = conf['dino_prompt']
text_prompt_as_point = list(filter(lambda x: x, conf['dino2sam_as_point'].split('.') ))
text_prompt_ignore = list(filter(lambda x: x, conf['dino2sam_ignore'].split('.') ))
print(f'Process as points: {text_prompt_as_point}')
print(f'Don\'t process: {text_prompt_ignore}')

# Function to generate mask image
def generate_sam_target_mask(image_path:str, text_prompt:str) -> np.ndarray:
    # Load the image
    image_np, image_as_tensor = load_image(image_path)
    
    # Predict the bounding boxes and labels using Grounding DINO
    boxes, logits, captions = predict(dino_model, image_as_tensor, text_prompt, 0.3, 0.3)

    h, w, _ = image_np.shape

    # Set the image for SAM predictor
    sam_predictor.set_image(image_np)

    # Convert boxes to the format required by SAM (as ndarray)
    sam_boxes = boxes.numpy()

    for i in range(sam_boxes.shape[0]):
        sam_boxes[i] = np.multiply(sam_boxes[i], np.array([w, h, w, h])) # Convert from normalized to original size
        # CxCyHW -> XYXY
        sam_boxes[i][:2] -= sam_boxes[i][2:] / 2
        sam_boxes[i][2:] += sam_boxes[i][:2]

    if boxes.shape[0] > 0:
        print(f'Grounding DINO got {boxes.shape[0]} boxes.')
    else:
        print('No boxes are detected by Grounding DINO, please check the threshold value.')
        return
 
    # Generate masks using SAM
    masks, _, _ = sam_predictor.predict(None, None, sam_boxes, None, False)

    # Combine masks into one ndarray
    combined = combine_masks_as_ndarray(masks)

    #plt.imshow(combined)
    #plt.show()

    return combined

def get_dots_from_box_and_mask(box: torch.Tensor, sam_target_mask: np.ndarray):
    im_size = sam_target_mask.shape # HxW
    print(f'Mask size: {im_size}') 

    h = im_size[0]
    w = im_size[1]

    box = box * torch.Tensor([w, h, w, h])
    box_xywh = box_convert(boxes=box, in_fmt="cxcywh", out_fmt="xywh").numpy()
    box_x = int(box_xywh[0])
    box_y = int(box_xywh[1])
    box_w = int(box_xywh[2])
    box_h = int(box_xywh[3])
    #print(f'Box: X: {box_x}, Y: {box_y}, W: {box_w}, H: {box_h}')

    m_dot_list = []

    for _ in range(3):
        accepted = False
        while not accepted:
            i_w = random.randint(0, box_w - 1)
            i_h = random.randint(0, box_h - 1)
            
            if sam_target_mask[box_y + i_h, box_x + i_w] > 0.5: # Point is in the target mask
                m_dot_list.append([box_x + i_w, box_y + i_h]) # XY

                accepted = True
    
    return m_dot_list

# Function to generate mask image
def generate_mask(image_path:str, text_prompt:str, dilate_amount:int = 0,
                  preview_each:bool = False, sam_target_mask: np.ndarray | None = None) -> np.ndarray:
    # Load the image
    image_np, image_as_tensor = load_image(image_path)
    
    # Predict the bounding boxes and labels using Grounding DINO
    boxes, logits, captions = predict(dino_model, image_as_tensor, text_prompt, 0.3, 0.3)

    if boxes.shape[0] == 0:
        print('No boxes are detected by Grounding DINO, please check the threshold value.')
        return

    h, w, _ = image_np.shape

    # Set the image for SAM predictor
    sam_predictor.set_image(image_np)

    # Convert boxes to the format required by SAM (as ndarray)
    #sam_boxes = deepcopy(boxes)
    
    print(f'Dino boxes shape: {boxes.shape}')
    sam_box_list = [] # Bx4, Mask count * 4 [Box shape]
    sam_dot_coord_list = [] # Bx10x2, Mask count * 10 [Points per mask] * 2
    sam_dot_box_list = [] # Bx10x2, Mask count * 10 [Points per mask] * 2

    random.seed(42)

    # Drop those boxes whose caption is set to be processed as points
    for i in range(boxes.size(0)):
        print(f'box[{i}]: {captions[i]}')

        if check_box_text_prompt(captions[i], text_prompt_as_point):
            print(f'Dotting {captions[i]}...')
            sam_dot_coord_list.append(get_dots_from_box_and_mask(boxes[i], sam_target_mask))
            #print(sam_dot_list)
            sam_dot_box_list.append(boxes[i])
        elif check_box_text_prompt(captions[i], text_prompt_ignore):
            # Do nothing
            print(f'Skipping {captions[i]}...')
        else:
            sam_box_list.append(boxes[i])
    # Stack them up as one Tensor. https://discuss.pytorch.org/t/how-to-convert-a-list-of-tensors-to-a-pytorch-tensor/175666
    sam_boxes = np.array(sam_box_list) # Bx4, Mask count * CxCyHW
    
    for i in range(sam_boxes.shape[0]): 
        sam_boxes[i] = np.multiply(sam_boxes[i], np.array([w, h, w, h])) # Convert from normalized to original size
        # CxCyHW -> XYXY
        sam_boxes[i][:2] -= sam_boxes[i][2:] / 2
        sam_boxes[i][2:] += sam_boxes[i][:2]

    if len(sam_dot_box_list) > 0:
        sam_dot_boxes = np.array(sam_dot_box_list) # Bx4, Mask count * CxCyHW
        for i in range(sam_dot_boxes.shape[0]): 
            sam_dot_boxes[i] = np.multiply(sam_dot_boxes[i], np.array([w, h, w, h])) # Convert from normalized to original size
            # CxCyHW -> XYXY
            sam_dot_boxes[i][:2] -= sam_dot_boxes[i][2:] / 2
            sam_dot_boxes[i][2:] += sam_dot_boxes[i][:2]
    else:
        sam_dot_boxes = None
 
    # Generate masks using SAM
    masks_from_boxes, _, _ = sam_predictor.predict(None, None, sam_boxes, None, False)
    if len(sam_dot_coord_list) > 0:
        sam_dot_coords = np.asarray(sam_dot_coord_list) # sam_predictor.transform.apply_coords(np.asarray(sam_dot_coord_list), image_np.shape[:2])
        sam_dot_labels = np.tile(1, (sam_dot_coords.shape[0], sam_dot_coords.shape[1])) # Labels, 0 for background, 1 for foreground
        #print(f'Shape of sam dots: {sam_dots.size()} | {sam_dot_labels.size()}') # BxNx2, BxN
        masks_from_dots, _, _ = sam_predictor.predict(sam_dot_coords, sam_dot_labels, sam_dot_boxes, None, False)
    else:
        masks_from_dots = None
    
    # Combine masks into one ndarray
    combined_mask = combine_masks_as_ndarray(masks_from_boxes)
    if masks_from_dots is not None and masks_from_dots.any():
        combined_mask_from_dots = combine_masks_as_ndarray(masks_from_dots)
        combined_mask = np.maximum(combined_mask, combined_mask_from_dots)
    
    # Dilate the mask
    if dilate_amount > 0:
        combined_mask = dilate_mask(combined_mask, dilate_amount)

    # Convert to rgb image (still as np array)
    mask_as_image_np = np.stack((combined_mask.astype(np.uint8) * 255,) * 3, axis=-1)

    if preview_each:
        processed = np.maximum(mask_as_image_np, image_np)

        # Annotate the masked image with the predicted boxes and labels
        image_np_annotated = cv2.cvtColor(annotate(
                processed, boxes, logits, captions), cv2.COLOR_RGB2BGR)
        
        # Visualize SAM prompt points
        for gi in range(len(sam_dot_coord_list)):
            for dot in sam_dot_coord_list[gi]:
                print(dot)
                dx, dy = dot[0], dot[1]

                for xi in range(-19, 20):
                    for yi in range(-19, 20):
                        if dx + xi >= 0 and dx + xi < w and dy + yi >= 0 and dy + yi < h:
                            image_np_annotated[dy + yi, dx + xi] = [255, 127, 255]

        plt.imshow(image_np_annotated)
        plt.show()

    return mask_as_image_np

import glob

handled_extn_names = conf['accept_types']
dir_i = conf['dir_i']

for file in glob.glob(f'{dir_i}/*'):
    file = file.replace('\\', '/')
    file_name = file.rsplit('/', 1)[1]
    ext_index = file_name.rfind('.')

    base_name = file_name[:ext_index].lower()
    extn_name = file_name[ext_index:].lower()

    if not base_name.endswith('_mask') and extn_name in handled_extn_names:
        print(f'Processing [{base_name}][{extn_name}]...')
        source_path = f'{dir_i}/{base_name}{extn_name}'
        output_path = f'{dir_i}/{base_name}_mask.png'

        sam_target_mask = generate_sam_target_mask(source_path, conf['dino2sam_target_object'])
        #sam_target_mask = None
        mask_as_image_np = generate_mask(source_path, text_prompt, preview_each=False,
                                         dilate_amount=25, sam_target_mask=sam_target_mask)

        # Save generated mask
        mask_image = Image.fromarray(mask_as_image_np)
        mask_image.save(output_path)
        
        print(f'Saved to {output_path}')