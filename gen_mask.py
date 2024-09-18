import json

with open("config.json") as f:
    conf = json.load(f)
    
import torch
from local_groundingdino.util.inference import load_model as load_dino_model, load_image, predict, annotate
from segment_anything import SamPredictor, sam_model_registry
from scipy.ndimage import binary_dilation
from PIL import Image
import matplotlib.pyplot as plt
import numpy as np
import cv2
from copy import deepcopy

# Disable Torch warnings
import warnings
torch.set_warn_always(False)
warnings.filterwarnings("ignore", category=FutureWarning)
warnings.filterwarnings("ignore", category=UserWarning)

# Load the Grounding DINO model
dino_model = load_dino_model(conf['dino_config'], conf['dino_model'])

# Load the SAM model
sam_checkpoint = conf['sam_model']
sam_model_type = conf['sam_model_type']

device = conf['mask_gen_device']

sam = sam_model_registry[sam_model_type](checkpoint=sam_checkpoint)
sam.to(device=device)

sam_predictor = SamPredictor(sam)

def combine_masks(mask_tensor):
    # Assuming mask_tensor is a numpy array of shape (B, C, H, W)
    B, C, H, W = mask_tensor.shape
    combined_masks = np.zeros((H, W))

    for b in range(B):
        for c in range(C):
            combined_masks = np.maximum(combined_masks, mask_tensor[b, c])

    return combined_masks

def dilate_mask(mask, dilation):
    x, y = np.meshgrid(np.arange(dilation), np.arange(dilation))
    center = dilation // 2
    dilation_kernel = ((x - center)**2 + (y - center)**2 <= center**2).astype(np.uint8)
    dilated_mask = binary_dilation(mask, dilation_kernel)

    return dilated_mask

text_prompt = conf['dino_prompt']

# Function to generate mask image
def generate_mask(image_path, text_prompt, output_path, preview_each):
    # Load the image
    image_np, image_as_tensor = load_image(image_path)
    
    # Predict the bounding boxes and labels using Grounding DINO
    boxes, logits, phrases = predict(dino_model, image_as_tensor, text_prompt, 0.3, 0.3)

    h, w, _ = image_np.shape

    # Set the image for SAM predictor
    sam_predictor.set_image(image_np)

    # Convert boxes to the format required by SAM (as Torch tensor)
    sam_boxes = deepcopy(boxes)

    for i in range(sam_boxes.size(0)):
        sam_boxes[i] = sam_boxes[i] * torch.Tensor([w, h, w, h])
        sam_boxes[i][:2] -= sam_boxes[i][2:] / 2
        sam_boxes[i][2:] += sam_boxes[i][:2]

    if boxes.shape[0] > 0:
        #print('Image shape: ' + str(image_np.shape[:2]))
        sam_boxes = sam_predictor.transform.apply_boxes_torch(sam_boxes, image_np.shape[:2])
    else:
        print('No boxes are detected by Grounding DINO, please check the threshold value.')
        return

    sam_boxes = sam_boxes.to(device)
    
    # Generate masks using SAM
    masks, _, _ = sam_predictor.predict_torch(None, None, sam_boxes, None, False)

    # Combine masks into one ndarray
    combined_mask = combine_masks(masks.cpu().numpy())
    dilated_mask = dilate_mask(combined_mask, 25)

    mask_as_image_np = np.stack((dilated_mask.astype(np.uint8) * 255,) * 3, axis=-1)

    if preview_each:
        # Annotate the masked image with the predicted boxes and labels
        image_np_annotated = cv2.cvtColor(annotate(
                np.maximum(mask_as_image_np, image_np), boxes, logits, phrases), cv2.COLOR_RGB2BGR)

        plt.imshow(image_np_annotated)
        plt.show()

    # Save generated mask
    mask_image = Image.fromarray(mask_as_image_np)
    mask_image.save(output_path)
    
    print(f'Saved to {output_path}')

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

        generate_mask(source_path, text_prompt, output_path, False)

