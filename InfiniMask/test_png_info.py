from PIL import Image
import re
import json

#img = Image.open('C:/Users/DevBo/Downloads/00000-3206416230.png')
img = Image.open('G:/BulkMagic/test/2373_bare/1_bare[42].png')

img.load() # necessary for png images to prepare the info data

img_p:list[str] = img.info['parameters'].split('\n')

prompt = img_p[0].split(',')
print(f'Prompt: {prompt}\n')

if len(img_p) > 1 and img_p[1].lower().startswith('negative prompt:'):
    neg_prompt = img_p[1].strip().split(',')
    print(f'Neg Prompt: {neg_prompt}\n')

    params_text = img_p[2]
else:
    params_text = img_p[1]


def parse_parameters(param_string):
    # This regex matches key-value pairs, even if values are quoted and contain nested parameters
    pattern = r'(\w+)\s*:\s*(?:"([^"]+)"|([^,]+))'
    
    matches = re.finditer(pattern, param_string)
    params = {}

    for match in matches:
        key = match.group(1)
        value = match.group(2) if match.group(2) is not None else match.group(3)
        
        # If the value contains nested parameters, parse them recursively
        if ':' in value:
            nested_params = parse_parameters(value)
            params[key] = nested_params
        else:
            params[key] = value.strip()
            
    return params


other_param_obj = parse_parameters(params_text)
other_param_json = json.dumps(other_param_obj, indent=2)

print(f'Other params: {other_param_json}\n')