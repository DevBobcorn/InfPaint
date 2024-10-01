import json

with open("config.json") as f:
    conf = json.load(f)

import socket
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

HOST = "127.0.0.1"  # Standard loopback interface address (localhost)
PORT = conf['mask_api_port']  # Port to listen on (non-privileged ports are > 1023)

# Not sure what it is. See https://github.com/eriklindernoren/PyTorch-YOLOv3/issues/162
#from PIL import ImageFile
#ImageFile.LOAD_TRUNCATED_IMAGES = True

def recv_image_bytes(conn) -> bytes:
    image_size = int.from_bytes(conn.recv(4), 'big') # 4B, unsigned big-endian
    image_bytes = conn.recv(image_size)
    print(f'Received image size: {len(image_bytes)}')

    return image_bytes

def recv_ascii_text(conn) -> str:
    text_size = int.from_bytes(conn.recv(4), 'big') # 4B, unsigned big-endian
    return conn.recv(text_size).decode(encoding='ascii')

def send_ascii_text(conn, text: str):
    text_bytes = text.encode(encoding='ascii')
    conn.sendall(len(text_bytes).to_bytes(4, 'big'))
    conn.sendall(text_bytes)

def recv_utf8_text(conn) -> str:
    text_size = int.from_bytes(conn.recv(4), 'big') # 4B, unsigned big-endian
    return conn.recv(text_size).decode(encoding='utf-8')

def send_utf8_text(conn, text: str):
    text_bytes = text.encode(encoding='utf-8')
    conn.sendall(len(text_bytes).to_bytes(4, 'big'))
    conn.sendall(text_bytes)

def send_mask_creator_args(conn):
    send_utf8_text(conn, conf['dir_i'])
    send_ascii_text(conn, conf['dino_prompt'])

def generate_masks(conn):
    image_bytes = recv_image_bytes(conn)

    image_pil = Image.open(io.BytesIO(image_bytes))
    image_np = np.asarray(image_pil.convert('RGB'))

    # Set image for SAM
    sam_predictor.set_image(image_np)

    control_flag = int.from_bytes(conn.recv(1)) # 1B

    logit_input = None

    if (control_flag & 1) != 0: # Points
        input_point = []
        input_label = []

        point_count = int.from_bytes(conn.recv(4), 'big') # 4B, unsigned big-endian

        for i in range(point_count):
            x = int.from_bytes(conn.recv(4), 'big') # 4B, unsigned big-endian
            y = int.from_bytes(conn.recv(4), 'big') # 4B, unsigned big-endian
            label = int.from_bytes(conn.recv(1)) # 1B

            print(f'Point: {x}, {y} [{label}]')

            input_point.append([x, y])
            input_label.append(label)
        
        input_point_np = np.array(input_point)
        input_label_np = np.array(input_label)
    else:
        input_point_np = None
        input_label_np = None
    
    if (control_flag & 2) != 0: # Box
        x1 = int.from_bytes(conn.recv(4), 'big') # 4B, unsigned big-endian
        y1 = int.from_bytes(conn.recv(4), 'big') # 4B, unsigned big-endian
        x2 = int.from_bytes(conn.recv(4), 'big') # 4B, unsigned big-endian
        y2 = int.from_bytes(conn.recv(4), 'big') # 4B, unsigned big-endian

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

    mask_count = masks.shape[0]
    conn.sendall(mask_count.to_bytes(4, 'big'))
    
    for i in range(mask_count):
        # Convert to rgb image (still as np array)
        mask_as_image_np = np.stack((masks[i].astype(np.uint8) * 255,) * 3, axis=-1)
        mask_score_as_int = int(1000000 * scores[i])
        print('Mask image shape: ' + str(mask_as_image_np.shape) + ', Score: ' + str(scores[i]))

        byte_buffer = io.BytesIO()

        mask_pil = Image.fromarray(mask_as_image_np)
        mask_pil.save(byte_buffer, format='PNG')
        mask_png_bytes = byte_buffer.getvalue()
        mask_png_size = len(mask_png_bytes)

        conn.sendall(mask_score_as_int.to_bytes(4, 'big'))
        conn.sendall(mask_png_size.to_bytes(4, 'big'))
        conn.sendall(mask_png_bytes)

def generate_box_layers(conn):
    image_bytes = recv_image_bytes(conn)
    text_prompt = recv_ascii_text(conn)
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
    
    # Send box layer count
    conn.sendall(int(sam_boxes.shape[0]).to_bytes(4, 'big'))

    box_scores = logits.numpy()

    for i in range(sam_boxes.shape[0]):
        x1, y1 = int(sam_boxes[i][0]), int(sam_boxes[i][1])
        x2, y2 = int(sam_boxes[i][2]), int(sam_boxes[i][3])

        print(f'Box [{captions[i]}]: {x1}, {y1}, {x2}, {y2} (Score: {box_scores[i]:.3f})')
        send_ascii_text(conn, captions[i] + f' ({box_scores[i]:.3f})')

        conn.sendall(x1.to_bytes(4, 'big'))
        conn.sendall(y1.to_bytes(4, 'big'))
        conn.sendall(x2.to_bytes(4, 'big'))
        conn.sendall(y2.to_bytes(4, 'big'))

        input_box_np = np.array([x1, y1, x2, y2])

        masks, scores, logits = sam_predictor.predict(
            box=input_box_np,
            mask_input=None,
            multimask_output=True,
        )

        mask_count = masks.shape[0]
        conn.sendall(mask_count.to_bytes(4, 'big'))
        
        for i in range(mask_count):
            # Convert to rgb image (still as np array)
            mask_as_image_np = np.stack((masks[i].astype(np.uint8) * 255,) * 3, axis=-1)
            mask_score_as_int = int(1000000 * scores[i])
            print('Mask image shape: ' + str(mask_as_image_np.shape) + ', Score: ' + str(scores[i]))

            byte_buffer = io.BytesIO()

            mask_pil = Image.fromarray(mask_as_image_np)
            mask_pil.save(byte_buffer, format='PNG')
            mask_png_bytes = byte_buffer.getvalue()
            mask_png_size = len(mask_png_bytes)

            conn.sendall(mask_score_as_int.to_bytes(4, 'big'))
            conn.sendall(mask_png_size.to_bytes(4, 'big'))
            conn.sendall(mask_png_bytes)


with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:

    sock.bind((HOST, PORT))
    sock.listen()
    print(f'Server listening on {HOST}:{PORT}')

    while True:
        conn, addr = sock.accept()

        with conn:
            print(f"Connected with {addr}")

            start_seq = [ 42, 20, 77, 13, 37 ]
            dont_read_nxt = False

            nxt_byte = 0

            try:
                while True:
                
                    # Find next request start
                    if not dont_read_nxt:
                        nxt_byte = int.from_bytes(conn.recv(1))
                    else:
                        dont_read_nxt = False # Reset flag

                    while nxt_byte != start_seq[0]:
                        nxt_byte = int.from_bytes(conn.recv(1))

                    match_idx = 0

                    while match_idx < len(start_seq) - 1 and nxt_byte == start_seq[match_idx]:
                        #print(f'Matched [{match_idx}]: {nxt_byte}')
                        nxt_byte = int.from_bytes(conn.recv(1))

                        match_idx += 1

                    if match_idx == len(start_seq) - 1: # Match found!
                        process_type = int.from_bytes(conn.recv(1))
                        print(f'============== Process Type: [{process_type}]')

                        if   process_type == 100: # Disconnect request
                            break
                        elif process_type == 101: # GenerateMasks request
                            generate_masks(conn)
                        elif process_type == 102: # GenerateBoxLayers request
                            generate_box_layers(conn)
                        elif process_type == 200: # GetStartupArgs request
                            send_mask_creator_args(conn)
                        else:
                            print(f'Undefined process type: {process_type}')

                    else: # Failed to match sequence
                        # The current byte might be the actual first byte of start_seq
                        dont_read_nxt = True
                        continue
                        
            except IOError as e:
                print(f'IO Error: {e}')

            print(f'Disconnected with {addr}')