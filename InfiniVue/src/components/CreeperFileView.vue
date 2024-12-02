<script setup>
import { useTemplateRef } from 'vue';
import { ref } from 'vue';

const prevImageRef = useTemplateRef('prev-image-ref');
const maskGraphicRef = useTemplateRef('mask-graphic-ref');

const props = defineProps([
  'staticroot',
  'activeprev',
  'activepath',
  'activetype',
  'prevlist',
  'imagedata',
  'maskdata',
]);

const emit = defineEmits([
  'updateActiveFile',
  'genBoxLayers',
  'genLayerMask',
]);

const updateActiveFile = (newItem) => {
  // Emit an event of the parent component to update the active file
  emit('updateActiveFile', newItem.prev, newItem.path, 'file');
};

const getImagePixel = (elem, eX, eY, discardOutside = true) => {
  const rect = elem.getBoundingClientRect();
  const aspectRatioElm = elem.clientWidth / elem.clientHeight;
  const aspectRatioSrc = elem.naturalWidth / elem.naturalHeight;

  var displayedWidth = 0, displayedHeight = 0, scaleRatio = 1;
  var actualX = 0, actualY = 0;

  if (aspectRatioElm >= aspectRatioSrc) {
    // Displayed height = Client height
    displayedHeight = elem.clientHeight;
    displayedWidth = displayedHeight * aspectRatioSrc;

    actualX = Math.floor(rect.left + (elem.clientWidth - displayedWidth) / 2);
    actualY = Math.floor(rect.top);

    scaleRatio = elem.naturalHeight / displayedHeight;
  } else {
    // Displayed width = Client width
    displayedWidth = elem.clientWidth;
    displayedHeight = displayedWidth / aspectRatioSrc;

    actualX = Math.floor(rect.left);
    actualY = Math.floor(rect.top + (elem.clientHeight - displayedHeight) / 2);

    scaleRatio = elem.naturalWidth / displayedWidth;
  }

  var domX = eX + window.scrollX - actualX;
  var domY = eY + window.scrollY - actualY;
  var pixelX = Math.floor(domX * scaleRatio);
  var pixelY = Math.floor(domY * scaleRatio);

  if (pixelX < 0 || pixelX >= elem.naturalWidth || pixelY < 0 || pixelY >= elem.naturalHeight) {

    if (discardOutside) { // Discard
      return null;
    } else { // Clamp
      const pixelXClamped = Math.max(Math.min(pixelX, elem.naturalWidth - 1), 0);
      const pixelYClamped = Math.max(Math.min(pixelY, elem.naturalHeight - 1), 0);
      return [ pixelXClamped, pixelYClamped ];
    }
  }

  return [ pixelX, pixelY ];
};

const handleImageLoad = (e) => {
  var elem = e.target;

  // Update image size
  props.imagedata.width = elem.naturalWidth;
  props.imagedata.height = elem.naturalHeight;

  // Do we have to use fetch to get image content (again) here?
  fetch(elem.src)
    .then((res) => res.blob())
    .then((blob) => {
        // Read the Blob as DataURL using the FileReader API
        const reader = new FileReader();

        reader.onloadend = () => {
            const b64 = reader.result.replace(/^data:(.+);base64,/, "");
            // Update image data
            props.imagedata.base64 = b64;
        };

        reader.readAsDataURL(blob);
    });

  console.log(`Image loaded, width: ${elem.naturalWidth}, height: ${elem.naturalHeight}`);
};

const handleMouseMove = (e) => {
  // drag event doesn't get fired properly, so we do it here
  if (dragData.value.dragging) {
    resizeBoxHint(e.target, e.x, e.y);
  }

  const imPixel = getImagePixel(e.target, e.x, e.y);
  if (imPixel == null) {
    return handleMouseLeave(e);
  }
  const [ pixelX, pixelY ] = imPixel;
  
  props.imagedata.messageText = `${pixelX}, ${pixelY} (${e.target.naturalWidth}x${e.target.naturalHeight})`;
};

const dragData = ref({
  dragging: false,
  startX: null,
  startY: null,

  boxShape: null,
});

const handleDragStart = (e) => {
  e.preventDefault();

  const clampedImPixel = getImagePixel(e.target, e.x, e.y, false);
  const [ pixelX, pixelY ] = clampedImPixel;

  props.imagedata.messageText = `${pixelX}, ${pixelY} (${e.target.naturalWidth}x${e.target.naturalHeight})`;

  if (props.maskdata.activeLayerIndex < 0) {
    return;
  }
  const activeLayer = props.maskdata.layerList[props.maskdata.activeLayerIndex];

  // Box layer allow only up to 1 box control, so check if we have one already
  if (activeLayer.type != 'box_with_points' || activeLayer.controls.some(x => x.type == 'box')) {
    return;
  }

  dragData.value.dragging = true;
  dragData.value.startX = pixelX;
  dragData.value.startY = pixelY;

  const boxHint = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
  boxHint.style.stroke = 'orange';
  boxHint.style.strokeWidth = 5;
  boxHint.style.fillOpacity = 0;

  // Update selection box
  boxHint.setAttribute('width', 0);
  boxHint.setAttribute('height', 0);
  boxHint.setAttribute('x', pixelX);
  boxHint.setAttribute('y', pixelY);

  dragData.value.boxShape = boxHint;
  maskGraphicRef.value.appendChild(boxHint);
};

const handleTouchStart = (e) => {
  const touch = e.changedTouches[0];

  handleDragStart({
    preventDefault: () => { },
    target: e.target,
    x: touch.clientX,
    y: touch.clientY,
  });
};

const handleTouchMove = (e) => {
  const touch = e.changedTouches[0];

  handleMouseMove({
    target: e.target,
    x: touch.clientX,
    y: touch.clientY,
  });
};

const handleTouchEnd = (e) => {
  const touch = e.changedTouches[0];

  if (dragData.value.dragging) {
    handleClick({
      target: e.target,
      x: touch.clientX,
      y: touch.clientY,
    });
  }
};

const resizeBoxHint = (elem, cursorX, cursorY) => {

  const clampedImPixel = getImagePixel(elem, cursorX, cursorY, false);
  const [ pixelX, pixelY ] = clampedImPixel;

  if (props.maskdata.activeLayerIndex < 0) {
    return;
  }
  const activeLayer = props.maskdata.layerList[props.maskdata.activeLayerIndex];

  if (activeLayer.type != 'box_with_points') {
    return;
  }

  const boxHint = dragData.value.boxShape;

  const minX = Math.min(dragData.value.startX, pixelX);
  const minY = Math.min(dragData.value.startY, pixelY);
  const maxX = Math.max(dragData.value.startX, pixelX);
  const maxY = Math.max(dragData.value.startY, pixelY);
  
  // Update selection box
  boxHint.setAttribute('width', maxX - minX);
  boxHint.setAttribute('height', maxY - minY);
  boxHint.setAttribute('x', minX);
  boxHint.setAttribute('y', minY);
};

const addControlToSvg = (svg, control) => {
  if (control.type == 'point') {
    const newShape = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
    newShape.setAttribute('cx', control.x);
    newShape.setAttribute('cy', control.y);
    newShape.setAttribute('r', 10);
    newShape.style.fill = control.label ? 'green' : 'red';

    svg.appendChild(newShape);
  } else if (control.type == 'box') {
    const newShape = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
    newShape.style.stroke = 'orange';
    newShape.style.strokeWidth = 5;
    newShape.style.fillOpacity = 0;

    newShape.setAttribute('width', control.maxX - control.minX);
    newShape.setAttribute('height', control.maxY - control.minY);
    newShape.setAttribute('x', control.minX);
    newShape.setAttribute('y', control.minY);

    svg.appendChild(newShape);
  } else {
    control.log(`Unsupported control type: ${control.type}`);
  }
};

const handleClick = (e) => {
  const imPixel = getImagePixel(e.target, e.x, e.y, false);
  const [ pixelX, pixelY ] = imPixel;

  props.imagedata.messageText = `${pixelX}, ${pixelY} (${e.target.naturalWidth}x${e.target.naturalHeight})`;

  if (props.maskdata.activeLayerIndex < 0) {
    return;
  }
  const activeLayer = props.maskdata.layerList[props.maskdata.activeLayerIndex];

  if (activeLayer.type != 'points' && activeLayer.type != 'box_with_points') {
    return;
  }

  const minX = Math.min(dragData.value.startX, pixelX);
  const minY = Math.min(dragData.value.startY, pixelY);
  const maxX = Math.max(dragData.value.startX, pixelX);
  const maxY = Math.max(dragData.value.startY, pixelY);

  if (dragData.value.dragging) {
    // Reset dragging flag
    dragData.value.dragging = false;

    const newBox = {
      name: `Box (${minX}, ${minY}, ${maxX}, ${maxY})`,
      type: 'box',
      minX: minX,
      minY: minY,
      maxX: maxX,
      maxY: maxY,
    };

    // Reset start point
    dragData.value.startX = null;
    dragData.value.startY = null;

    // The shape is already there, just update it
    const boxHint = dragData.value.boxShape;
    boxHint.setAttribute('width', maxX - minX);
    boxHint.setAttribute('height', maxY - minY);
    boxHint.setAttribute('x', minX);
    boxHint.setAttribute('y', minY);

    dragData.value.boxShape = null; // Remove reference to this shape object

    activeLayer.controls.push(newBox);
  } else {
    const pointLabel = true;
    const newPoint = {
      name: `Point (${pixelX}, ${pixelY}, ${pointLabel})`,
      type: 'point',
      x: pixelX,
      y: pixelY,
      label: pointLabel // true for positive, false for negative
    };

    addControlToSvg(maskGraphicRef.value, newPoint);
    activeLayer.controls.push(newPoint);
  }
};

const handleMouseLeave = (_) => {
  props.imagedata.messageText = '';
};

const addBoxLayer = () => {
  const dataUrl = createImageOfSize(props.imagedata.width, props.imagedata.height);
  const newLayer = {
    name: `Box Layer`,
    type: 'box_with_points',
    controls: [ ],
    selectedMaskIndex: 0,
    maskImages: [ dataUrl ]
  };
  props.maskdata.layerList.push(newLayer);

  // Select after creation
  focusMaskLayer(newLayer, props.maskdata.layerList.length - 1);
};

const addPtsLayer = () => {
  const dataUrl = createImageOfSize(props.imagedata.width, props.imagedata.height);
  const newLayer = {
    name: `Points Layer`,
    type: 'points',
    controls: [ ],
    selectedMaskIndex: 0,
    maskImages: [ dataUrl ]
  };
  props.maskdata.layerList.push(newLayer);

  // Select after creation
  focusMaskLayer(newLayer, props.maskdata.layerList.length - 1);
};

function compositeImagesArray(maskDataUrls) {

  // Create canvas elements
  const resultCanvas = document.createElement('canvas');
  const resultContext = resultCanvas.getContext('2d');

  // Store image size
  const width = props.imagedata.width;
  const height = props.imagedata.height;
  resultCanvas.width = width;
  resultCanvas.height = height;

  // Load images
  const images = maskDataUrls.map(_ => new Image());

  // Create canvases for each image (for reading pixel data)
  const canvases = maskDataUrls.map(_ => {
    const canvas = document.createElement('canvas');
    canvas.width  = width;
    canvas.height = height;

    return canvas;
  });
  const contexts = canvases.map(canvas => canvas.getContext('2d'));

  // Create result data
  const resultData = resultContext.createImageData(width, height);

  // Create a promise for each image, and resolve them by setting src for each image
  const imageLoadPromises = maskDataUrls.map((base64, index) => new Promise((resolve, reject) => {
    images[index].addEventListener('load', () => {
      resolve();
    });
    images[index].addEventListener('error', () => {
      console.error(`Mask image #${index} failed to load`);
      reject();
    });

    images[index].src = base64;
  }));

  // Wait for all images to load
  Promise.all(imageLoadPromises)
    .then(() => {

      // Draw each image onto their respective canvas
      images.forEach((image, index) => {
        contexts[index].drawImage(image, 0, 0);
      });

      // getImageData is an EXPENSIVE operation, DO NOT call this for every pixel!
      const imageDataArray = contexts.map(x => x.getImageData(0, 0, width, height));

      // Go through each pixel on the image
      for (let i = 0; i < resultData.data.length; i += 4) {
        let red = 0, green = 0, blue = 0;

        // For this pixel on each layer image
        imageDataArray.forEach(imageData => {
          red   += imageData.data[i];
          green += imageData.data[i + 1];
          blue  += imageData.data[i + 2];
        });

        resultData.data[i]     = Math.min(255, red);   // Red
        resultData.data[i + 1] = Math.min(255, green); // Green
        resultData.data[i + 2] = Math.min(255, blue);  // Blue
        resultData.data[i + 3] = 255;                  // Alpha
      }

      // Write composite data back to result context
      resultContext.putImageData(resultData, 0, 0);

      // Convert composited image to data url
      props.maskdata.maskPrevSrc = resultCanvas.toDataURL('image/png');
    });
}

const createImageOfSize = (width, height) => {
  // https://stackoverflow.com/a/72783044/21178367
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;

  const context = canvas.getContext('2d');
  context.fillStyle = '#000'; // All black
  context.fillRect(0, 0, width, height);

  return canvas.toDataURL('image/png');
};

const focusMaskLayer = (maskLayer, maskLayerIndex) => {
  // Clear all layer controls
  maskGraphicRef.value.innerHTML = '';
  
  if (props.maskdata.activeLayerIndex != maskLayerIndex && -1 != maskLayerIndex) {
    console.log(`Selected [${maskLayerIndex}] ${maskLayer.name}`);
    props.maskdata.activeLayerIndex = maskLayerIndex;

    // Load and show layer controls
    maskLayer.controls.forEach(control => addControlToSvg(maskGraphicRef.value, control));

    // Update main view
    props.maskdata.maskPrevSrc = maskLayer.maskImages[maskLayer.selectedMaskIndex];
  } else {
    console.log('Deselected');
    props.maskdata.activeLayerIndex = -1;

    // Update main view (Composite all mask layers)
    compositeImagesArray(props.maskdata
      .layerList.map(x => x.maskImages[x.selectedMaskIndex]));
  }
};

const removeMaskLayer = (maskLayerIndex) => {
  // Remove this layer
  props.maskdata.layerList.splice(maskLayerIndex, 1); // 2nd parameter means remove one item only

  if (props.maskdata.activeLayerIndex == maskLayerIndex) {
    // Clear all layer controls
    maskGraphicRef.value.innerHTML = '';
    
    // Active layer got removed, select first layer
    if (props.maskdata.layerList.length > 0) {
      focusMaskLayer(props.maskdata.layerList[0], 0);
    } else {
      focusMaskLayer(null, -1); // No layer left, deselect

      // Update main view (Composite all mask layers)
      compositeImagesArray(props.maskdata
        .layerList.map(x => x.maskImages[x.selectedMaskIndex]));
    }
  } else if (props.maskdata.activeLayerIndex > maskLayerIndex) { // Then move one left after removal
    props.maskdata.activeLayerIndex -= 1;

    // Nothing else needs to be done
  } else if (props.maskdata.activeLayerIndex < 0) {
    // Update main view (Composite all mask layers)
    compositeImagesArray(props.maskdata
        .layerList.map(x => x.maskImages[x.selectedMaskIndex]));
  }
};

const removeMaskControl = (maskControlIndex) => {
  const activeLayer = props.maskdata.layerList[props.maskdata.activeLayerIndex];

  if (maskControlIndex >= 0 && maskControlIndex < activeLayer.controls.length) {
    // Remove this layer
    activeLayer.controls.splice(maskControlIndex, 1); // 2nd parameter means remove one item only

    // Clear all layer controls
    maskGraphicRef.value.innerHTML = '';

    // Load and show layer controls
    activeLayer.controls.forEach(control => {
      addControlToSvg(maskGraphicRef.value, control);
    });
  }
};

defineExpose({
  focusMaskLayer,
  prevImageRef,
  maskGraphicRef
});

</script>

<template>
  <div class="outer-container">
    <div class="file-view-container">

      <img v-if="activetype == 'folder' || activetype == 'unknown' || activetype == 'none'"
        class="main-file-view-icon" :src="activeprev">

      <div class="main-file-view" v-if="activetype == 'image'">
        <img class="main-file-view-image" ref="prev-image-ref"
             :src="`${staticroot}${activepath}`"
             @load="handleImageLoad"
             @mousemove="handleMouseMove"
             @click="handleClick"
             @dragstart="handleDragStart"
             @touchstart="handleTouchStart"
             @touchmove="handleTouchMove"
             @touchend="handleTouchEnd"
             @touchcancel="handleTouchEnd"
             @mouseleave="handleMouseLeave" >

        <div class="main-file-view-overlay-div" v-if="maskdata.editing">
          <img class="main-file-view-overlay-image"
               v-if="maskdata.maskPrevSrc != ''"
               :src="maskdata.maskPrevSrc" >
        </div>

        <svg class="main-file-view-overlay-div" v-show="maskdata.editing"
             ref="mask-graphic-ref">
        </svg>

        <p class="main-file-view-message-text" ref="message-text">
          {{ imagedata.messageText }}
        </p>
      </div>

      <div v-if="maskdata.editing && maskdata.activeLayerIndex >= 0" class="mask-control-list">
        <span class="mask-control-list-item" v-for="(control, index) in maskdata.layerList[maskdata.activeLayerIndex].controls">
          <p style="width: 200px;">{{ control.name }}</p>
          <span @click="removeMaskControl(index)">[ X ]</span>
        </span>
      </div>

      <video v-if="activetype == 'video'" controls
        class="main-file-view">
        <source :src="`${staticroot}${activepath}`">
      </video>

    </div>

    <el-scrollbar v-if="!maskdata.editing">
      <div class="scrollbar-flex-content">
        <el-button plain v-for="item in prevlist"
          :key="item" @click="updateActiveFile(item)"
          :class="[{ 'active_prev_item': item.path == activepath }, 'prev_item']">
          <img :src="item.prev" class="scrollbar-item-image-view">
        </el-button>
      </div>
    </el-scrollbar>

    <div v-if="maskdata.editing" style="width: 100%; overflow: hidden;">

      <div class="ops-grid-container">

        <el-button class="ops-item1" @click="addPtsLayer"
                   v-if="maskdata.editing">+ Pts Layer</el-button>
        
        <el-button class="ops-item2" @click="addBoxLayer"
                   v-if="maskdata.editing">+ Box Layer</el-button>

        <el-input class="ops-item3" v-model="maskdata.dinoPrompt" 
                  v-if="maskdata.editing"></el-input>
    
        <el-button class="ops-item4" @click="emit('genBoxLayers')"
                   v-if="maskdata.editing" >Gen Boxes</el-button>
        
        <el-button class="ops-item5" @click="emit('genLayerMask')"
                   v-if="maskdata.editing" :disabled="maskdata.activeLayerIndex < 0" >Gen Masks</el-button>
      </div>

      <el-scrollbar class="ops-scrollbar">
        <div class="scrollbar-flex-content">
          <el-button plain v-for="(item, index) in maskdata.layerList"
            :key="item" @click="focusMaskLayer(item, index)"
            :class="[{ 'active_prev_item': index == maskdata.activeLayerIndex }, 'prev_item']">
            <img :src="item.maskImages[item.selectedMaskIndex]" class="scrollbar-item-image-view">
            <p>{{ item.name }}</p>

            <el-button plain class="prev_item_remove"
                       @click="removeMaskLayer(index)">X</el-button>

          </el-button>
        </div>
      </el-scrollbar>

    </div>

  </div>
</template>

<style scoped>
.outer-container {
  height: 100%;
}

.file-view-container {
  background-color: var(--el-fill-color-dark);
  border-radius: var(--el-border-radius-base);
  height: calc(100% - 130px);

  position: relative;
  display: flex;
  justify-content: center;
  align-items: center;
}

.main-file-view {
  position: relative;
  width: 95%;
  height: 95%;
  border-radius: var(--el-border-radius-base);
  object-fit: contain;
}

.main-file-view-icon {
  position: relative;
  width: 50%;
  height: 50%;
  border-radius: var(--el-border-radius-base);
  object-fit: contain;
}

.main-file-view-image {
  width: 100%;
  height: 100%;

  object-fit: contain;
}

.main-file-view-overlay-div {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  pointer-events: none;

  z-index: 1;
}

.main-file-view-overlay-image {
  width: 100%;
  height: 100%;
  opacity: 0.5;
  pointer-events: none;

  object-fit: contain;
}

.main-file-view-message-text {
  position: absolute;

  line-height: 1em;
  display: inline-block;
  background-color: white;

  margin: 0;
  padding: 3px;
  left: -3%;
  bottom: -3%;
  z-index: 200;
}

.mask-control-list {
  position: absolute;
  display: inline-grid;
  user-select: none;
  left: 0;
  top: 0;
  z-index: 300;
  color: white;
  background: rgba(0, 0, 0, 0.5);
}

.mask-control-list-item * {
  display: inline-block;
  height: 8px;
  margin: 5px;
  font-size: 14px;
}

.el-scrollbar {
  height: 100px;
}

.scrollbar-flex-content {
  display: flex;
  vertical-align: middle;
  align-items: center;
}

.prev_item {
  flex-shrink: 0;
  display: flex;
  position: relative;
  align-items: center;
  justify-content: center;
  width: 70px;
  height: 70px;
  margin: 20px 20px 0 0;
  text-align: center;
  border-radius: 4px;
  background: var(--el-fill-color-lighter);
}

.active_prev_item {
  background: var(--el-color-primary-light-7);
}

.prev_item p {
  font-size: 10px;
  position: absolute;
  top: -25px;
  left: 0px;
}

.prev_item_remove {
  position: absolute;
  top: -6px;
  right: -6px;
  width: 32px;
  height: 20px;
}

.scrollbar-item-image-view {
  margin: auto;
  max-width: 100%;
  max-height: 100%;
}

.ops-item1 { grid-area: r1c1; height: 30%; }
.ops-item2 { grid-area: r1c2; height: 30%; }
.ops-item3 { grid-area: r2; height: 30%; }
.ops-item4 { grid-area: r3c1; height: 30%; }
.ops-item5 { grid-area: r3c2; height: 30%; }

.ops-scrollbar {
  margin-right: 205px;
}

.ops-grid-container {
  margin-top: 10px;
  float: right;
  width: 200px;
  height: 100px;
  grid-template-areas:
    'r1c1 r1c2'
    'r2 r2'
    'r3c1 r3c2';
}

.ops-grid-container .el-input {
  margin-top: 2px;
  margin-left: 4px;
  width: calc(100% - 5px);

  resize: none;
  font-family: 'Helvetica Neue',Helvetica,'PingFang SC','Hiragino Sans GB','Microsoft YaHei','微软雅黑',Arial,sans-serif;
}

.ops-grid-container .el-button {
  margin-top: 2px;
  margin-left: 4px;

  font-size: 14px;

  width: calc(50% - 5px);
}

</style>