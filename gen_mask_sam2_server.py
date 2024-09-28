import socket
import io
import numpy as np
from PIL import Image, ImageFile
from sam2.build_sam import build_sam2
from sam2.sam2_image_predictor import SAM2ImagePredictor

# Disable Torch warnings
import warnings
import torch
torch.set_warn_always(False)
warnings.filterwarnings("ignore", category=FutureWarning)
warnings.filterwarnings("ignore", category=UserWarning)

# Load the SAM2 model
print('Loading SAM2...')

sam_checkpoint = "./models/sam2/sam2_hiera_large.pt"
sam_model_cfg = "sam2_hiera_l.yaml"
sam_predictor = SAM2ImagePredictor(build_sam2(sam_model_cfg, sam_checkpoint))

HOST = "127.0.0.1"  # Standard loopback interface address (localhost)
PORT = 65432  # Port to listen on (non-privileged ports are > 1023)

# Not sure what it is. See https://github.com/eriklindernoren/PyTorch-YOLOv3/issues/162
ImageFile.LOAD_TRUNCATED_IMAGES = True

with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:

    sock.bind((HOST, PORT))
    sock.listen()

    print(f'Server listening on {HOST}:{PORT}')

    while True:
        conn, addr = sock.accept()

        with conn:
            print(f"Connected with {addr}")

            try:
                image_size = int.from_bytes(conn.recv(4), 'big') # 4B, unsigned big-endian
                #print(f'Image size: {image_size}')
                if image_size > 0:
                    image_bytes = conn.recv(image_size)
                else:
                    break

                pil_image = Image.open(io.BytesIO(image_bytes))
                sam_predictor.set_image(pil_image)

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
                
                print('Processing... Please wait')
                
                masks, scores, logits = sam_predictor.predict(
                    point_coords=input_point_np,
                    point_labels=input_label_np,
                    box=input_box_np,
                    mask_input=logit_input[None, :, :] if logit_input is not None else None,
                    multimask_output=True,
                )

                print('Masks shape: ' + str(masks.shape))

                mask_count = masks.shape[0]
                conn.sendall(mask_count.to_bytes(4, 'big'))
                
                for i in range(mask_count):
                    # Convert to rgb image (still as np array)
                    mask_as_image_np = np.stack((masks[i].astype(np.uint8) * 255,) * 3, axis=-1)
                    print('Mask image shape: ' + str(mask_as_image_np.shape))

                    byte_buffer = io.BytesIO()

                    mask_pil = Image.fromarray(mask_as_image_np)
                    mask_pil.save(byte_buffer, format='PNG')
                    mask_png_bytes = byte_buffer.getvalue()
                    mask_png_size = len(mask_png_bytes)

                    conn.sendall(mask_png_size.to_bytes(4, 'big'))
                    conn.sendall(mask_png_bytes)
                    
            except Exception as e:
                print(e)

            finally:
                print(f'Closed connection with {addr}')