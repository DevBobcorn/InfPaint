import json
import sys, os

# Change cwd to script path
print(sys.path[0])
os.chdir(sys.path[0])

with open("config.json") as f:
    conf = json.load(f)

from flask import Flask, jsonify, request
from flask_cors import CORS
import urllib
import base64
import io
import copy
import numpy as np
from PIL import Image
from local_groundingdino.util.inference import load_model as load_dino_model, load_image_pil, predict
from sam2.build_sam import build_sam2
from sam2.sam2_image_predictor import SAM2ImagePredictor

# Disable Torch warnings
import warnings
import torch
torch.set_warn_always(False)
warnings.filterwarnings("ignore", category=FutureWarning)
warnings.filterwarnings("ignore", category=UserWarning)

# Load the Grounding DINO model
print('Loading Grouding DINO...')
dino_model = load_dino_model(conf['dino_config'], conf['dino_model'])

# Load the SAM2 model
print('Loading SAM2...')
sam_predictor = SAM2ImagePredictor(build_sam2(conf['sam_config'], conf['sam_model']))

HOST = conf['mask_api_host'] # Host address to run the server
PORT = conf['mask_api_port'] # Port to listen on (non-privileged ports are > 1023)

# Not sure what it is. See https://github.com/eriklindernoren/PyTorch-YOLOv3/issues/162
#from PIL import ImageFile
#ImageFile.LOAD_TRUNCATED_IMAGES = True

app = Flask(__name__)
CORS(app, origins='*')

@app.route('/generate_masks', methods = ['POST'])
def generate_masks():
    received_json = request.get_json()

    image_bytes = base64.b64decode(received_json['image_bytes'])

    image_pil = Image.open(io.BytesIO(image_bytes))
    image_np = np.asarray(image_pil.convert('RGB'))

    # Set image for SAM
    sam_predictor.set_image(image_np)

    control_flag = received_json['control_flag']

    logit_input = None

    if (control_flag & 1) != 0: # Points
        input_point = []
        input_label = []

        points = received_json['points'].split(',')

        if len(points) % 3 != 0:
            print(f'Unexpected length for point data array. Should be a multuple of 3, got {len(points)}.')
            return

        for i in range(0, len(points), 3):
            x = int(points[i])
            y = int(points[i + 1])
            label = int(points[i + 2])

            print(f'Point: {x}, {y} [{label}]')

            input_point.append([x, y])
            input_label.append(label)
        
        input_point_np = np.array(input_point)
        input_label_np = np.array(input_label)
    else:
        input_point_np = None
        input_label_np = None
    
    if (control_flag & 2) != 0: # Box

        box = received_json['box'].split(',')
        
        if len(box) != 4:
            print(f'Unexpected length for point data array. Should be 4, got {len(points)}.')
            return
        
        x1 = int(box[0])
        y1 = int(box[1])
        x2 = int(box[2])
        y2 = int(box[3])

        print(f'Box: {x1}, {y1}, {x2}, {y2}')
        
        input_box_np = np.array([x1, y1, x2, y2])
    else:
        input_box_np = None
    
    masks, scores, logits = sam_predictor.predict(
        point_coords=input_point_np,
        point_labels=input_label_np,
        box=input_box_np,
        mask_input=logit_input[None, :, :] if logit_input is not None else None,
        multimask_output=True,
    )

    result = { }
    result['masks'] = [ ]

    for i in range(masks.shape[0]):
        # Convert to rgb image (still as np array)
        mask_as_image_np = np.stack((masks[i].astype(np.uint8) * 255,) * 3, axis=-1)
        print('Mask image shape: ' + str(mask_as_image_np.shape) + ', Score: ' + str(scores[i]))

        byte_buffer = io.BytesIO()

        mask_pil = Image.fromarray(mask_as_image_np)
        mask_pil.save(byte_buffer, format='PNG')
        mask_png_bytes = byte_buffer.getvalue()

        result['masks'].append({
            'score': str(scores[i]),
            'bytes': base64.b64encode(mask_png_bytes).decode('ascii')
        })
    
    return jsonify(result)


@app.route('/generate_box_layers', methods = ['POST'])
def generate_box_layers():
    received_json = request.get_json()

    image_bytes = base64.b64decode(received_json['image_bytes'])
    text_prompt = received_json['text_prompt']

    print(f'Text prompt: {text_prompt}')

    # Load the image
    image_pil = Image.open(io.BytesIO(image_bytes))
    image_np, image_as_tensor = load_image_pil(image_pil)

    # Set image for SAM
    sam_predictor.set_image(image_np)

    h, w, _ = image_np.shape
    
    # Predict the bounding boxes and labels using Grounding DINO
    boxes, logits, captions = predict(dino_model, image_as_tensor, text_prompt, 0.3, 0.3)

    print(f'Dino boxes shape: {boxes.shape}')
    sam_boxes = copy.deepcopy(boxes) # Bx4, Mask count * 4 [Box shape]

    for i in range(sam_boxes.shape[0]):
        sam_boxes[i] = np.multiply(sam_boxes[i], np.array([w, h, w, h])) # Convert from normalized to original size
        # CxCyHW -> XYXY
        sam_boxes[i][:2] -= sam_boxes[i][2:] / 2
        sam_boxes[i][2:] += sam_boxes[i][:2]

    box_scores = logits.numpy()

    result = { }
    result['box_layers'] = [ ]

    for i in range(sam_boxes.shape[0]):
        x1, y1 = int(sam_boxes[i][0]), int(sam_boxes[i][1])
        x2, y2 = int(sam_boxes[i][2]), int(sam_boxes[i][3])

        print(f'Box [{captions[i]}]: {x1}, {y1}, {x2}, {y2} (Score: {box_scores[i]:.3f})')

        input_box_np = np.array([x1, y1, x2, y2])

        masks, scores, logits = sam_predictor.predict(
            box=input_box_np,
            mask_input=None,
            multimask_output=True,
        )

        box_obj = { }
        box_obj['caption'] = captions[i] + f' ({box_scores[i]:.3f})'
        box_obj['x1'] = x1
        box_obj['y1'] = y1
        box_obj['x2'] = x2
        box_obj['y2'] = y2

        box_obj['masks'] = [ ]
        
        for i in range(masks.shape[0]):
            # Convert to rgb image (still as np array)
            mask_as_image_np = np.stack((masks[i].astype(np.uint8) * 255,) * 3, axis=-1)
            print('Mask image shape: ' + str(mask_as_image_np.shape) + ', Score: ' + str(scores[i]))

            byte_buffer = io.BytesIO()

            mask_pil = Image.fromarray(mask_as_image_np)
            mask_pil.save(byte_buffer, format='PNG')
            mask_png_bytes = byte_buffer.getvalue()

            box_obj['masks'].append({
                'score': str(scores[i]),
                'bytes': base64.b64encode(mask_png_bytes).decode('ascii')
            })
        
        result['box_layers'].append(box_obj)
    
    return jsonify(result)


@app.route('/dino_default_prompt')
def defaultDinoPrompt():
    return urllib.parse.quote(conf['dino_default_prompt'])


if __name__ == '__main__':
    # Disable werkzeug reloading. See https://stackoverflow.com/a/9476701/21178367
    app.run(host=HOST, port=PORT, debug=True, use_reloader=False)