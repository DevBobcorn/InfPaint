import cv2
import os
import json
import numpy as np
from segment_anything import sam_model_registry, SamPredictor

with open("config.json") as f:
    conf = json.load(f)

dir_i = conf['dir_i']
display_scale = 0.375

image_files = [f for f in os.listdir(dir_i) if f.lower().endswith(('.png', '.jpg', '.jpeg'))]

device = conf['mask_gen_device']

# Load the SAM model
sam_checkpoint = conf['sam_model']
sam_model_type = conf['sam_model_type']

sam = sam_model_registry[sam_model_type](checkpoint=sam_checkpoint)
sam.to(device=device)

sam_predictor = SamPredictor(sam)

def mouse_click(event, x, y, flags, param):
    global input_point, input_label, input_paused

    x = int(x / display_scale)
    y = int(y / display_scale)

    if not input_paused:
        if event == cv2.EVENT_LBUTTONDOWN:
            input_point.append([x, y])
            input_label.append(1)
        elif event == cv2.EVENT_RBUTTONDOWN:
            input_point.append([x, y])
            input_label.append(0)
    else:
        if event == cv2.EVENT_LBUTTONDOWN or event == cv2.EVENT_RBUTTONDOWN:
            print('此时不能添加点,按w退出mask选择模式')

def apply_color_mask(image, mask, color, color_dark = 0.5):
    for c in range(3):
        image[:, :, c] = np.where(mask == 1, image[:, :, c] * (1 - color_dark) + color_dark * color[c], image[:, :, c])
    return image

def save_mask(mask, output_dir, filename):
    save_filename = filename[:filename.rfind('.')] + '_mask.png'
    cv2.imwrite(os.path.join(output_dir, save_filename), mask)
    
    print(f"Saved as {save_filename}")

current_index = 0

cv2.namedWindow("image")
cv2.setMouseCallback("image", mouse_click)
input_point = []
input_label = []
input_paused = False

while True:
    filename = image_files[current_index]
    image_original = cv2.imread(os.path.join(dir_i, filename))
    image_crop = image_original.copy()
    image = cv2.cvtColor(image_original.copy(), cv2.COLOR_BGR2RGB)
    selected_mask = None
    logit_input= None

    display_w = int(image_original.shape[1] * display_scale)
    display_h = int(image_original.shape[0] * display_scale)

    while True:
        input_paused = False
        image_overlayed = image_original.copy()
        
        # Press s to save | Press w to predict | Press d to next image | Press a to previous image
        # Press space to clear | Press q to remove last point

        for point, label in zip(input_point, input_label):
            color = (0, 255, 0) if label == 1 else (0, 0, 255)
            cv2.circle(image_overlayed, tuple(point), 15, color, -1)
        
        if selected_mask is not None:
            color = [255, 0, 0] # tuple(np.random.randint(0, 256, 3).tolist())
            apply_color_mask(image_overlayed, selected_mask, color)
        
        image_overlayed_display = cv2.resize(image_overlayed, (display_w, display_h))
        cv2.imshow("image", image_overlayed_display)
        key = cv2.waitKey(1)

        if key == ord(" "):
            input_point = []
            input_label = []
            selected_mask = None
            logit_input= None
        elif key == ord("w"):
            input_paused = True
            if len(input_point) > 0 and len(input_label) > 0:
                
                sam_predictor.set_image(image)
                input_point_np = np.array(input_point)
                input_label_np = np.array(input_label)

                masks, scores, logits= sam_predictor.predict(
                    point_coords=input_point_np,
                    point_labels=input_label_np,
                    mask_input=logit_input[None, :, :] if logit_input is not None else None,
                    multimask_output=True,
                )

                mask_idx = 0
                num_masks = len(masks)
                selected_mask = masks[mask_idx]

                while(1):
                    mask_color = tuple(np.random.randint(0, 256, 3).tolist())

                    image_overlayed = image_original.copy()
                    apply_color_mask(image_overlayed, selected_mask, mask_color)
                    image_overlayed_display = cv2.resize(image_overlayed, (display_w, display_h))
                    
                    display_info = f'Total: {num_masks} | Current: {mask_idx} | Score: {scores[mask_idx]:.2f}'
                    cv2.putText(image_overlayed_display, display_info, (10, 10), cv2.FONT_HERSHEY_PLAIN, 0.9, (0, 0, 0), 1, cv2.LINE_AA)

                    # Press w to confirm | Press d to next mask | Press a to previous mask | Press q to remove last point | Press s to save

                    cv2.imshow("image", image_overlayed_display)

                    key=cv2.waitKey(10)
                    if key == ord('q') and len(input_point) > 0:
                        input_point.pop(-1)
                        input_label.pop(-1)
                    elif key == ord('s'):
                        save_mask(image_crop, selected_mask, dir_i, filename)
                    elif key == ord('a'):
                        if mask_idx > 0:
                            mask_idx -= 1
                        else:
                            mask_idx = num_masks - 1
                        
                        selected_mask = masks[mask_idx]
                    elif key == ord('d'):
                        if mask_idx < num_masks-1:
                            mask_idx += 1
                        else:
                            mask_idx = 0
                        
                        selected_mask = masks[mask_idx]
                    elif key == ord('w'):
                        break
                    elif key == ord(" "):
                        input_point = []
                        input_label = []
                        selected_mask = None
                        logit_input = None
                        break
                logit_input = logits[mask_idx, :, :]
                print('max score:', np.argmax(scores), ' select:', mask_idx)

        elif key == ord('a'):
            current_index = max(0, current_index - 1)
            input_point = []
            input_label = []
            break
        elif key == ord('d'):
            current_index = min(len(image_files) - 1, current_index + 1)
            input_point = []
            input_label = []
            break
        elif key == 27:
            break
        elif key == ord('q') and len(input_point) > 0:
            input_point.pop(-1)
            input_label.pop(-1)
        elif key == ord('s') and selected_mask is not None:
            save_mask(image_crop, selected_mask, dir_i, filename)

    if key == 27:
        break