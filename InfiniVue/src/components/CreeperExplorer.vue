<script setup>
import { ref, computed } from 'vue';
import { onMounted, onUnmounted } from 'vue';

import CreeperFileTree from './CreeperFileTree.vue';
import CreeperFileView from './CreeperFileView.vue';
import PopContainer from './PopContainer.vue';

import 'element-plus/theme-chalk/display.css';

import { useTemplateRef } from 'vue';

const imageFiles = [
  'jpeg', 'jpg',  'gif',
  'png',  'webp', 'heif',
  'tiff', 'tif',  'bmp',
  'svg'
];

const videoFiles = [
  'mpeg', 'mpg',  'mpv',
  'mp4',  'm4p',  'm4v',
  'avi',  'wmv',  'mkv',
  'webm'
];

const audioFiles = [
  'flac', 'ogg',  'mp3',
  'wav'
];

const fileProps = {
  value: 'filePath',
  label: 'name',
  children: 'children'
};

const FOLDER_PREV = 'icons/folder.svg';
const FILE_UNKNOWN_PREV = 'icons/file-unknown-fill.svg';
const FILE_VIDEO_PREV = 'icons/file-exclamation-fill.svg';

const popFileTreeShown = ref(false);

const windowWidth = ref(window.innerWidth);
const windowHeight = ref(window.innerHeight);

const fileViewRef = useTemplateRef('file-view-ref');

// Use specified file server in dev mode (because it is different from Vite development server)
// See https://vitejs.dev/guide/env-and-mode.html
const fileServerHost = import.meta.env.DEV ? import.meta.env.VITE_DEV_FILE_SERVER : '';
console.log(`File server: [${fileServerHost}]`);

// Ask file server about them later
var maskServerHost = '';
var inPaintServerHost = '';

const handleResize = () => {
  windowWidth.value = window.innerWidth;
  windowHeight.value = window.innerHeight;

  // Update canvas size
  updateMaskGraphicSize();
};

const useMobileLayout = computed(() => windowWidth.value < 760);

const staticFileRoot = computed(() => `${fileServerHost}/files`);

const explorerData = ref({
  showFileTree: true,
  fileIndex: [ ],
  activePrev: `icons/folder.svg`,
  activePath: '',
  activeType: 'none',

  imageData: {
    width: 0,
    height: 0,
    base64: '',
    messageText: '',
  },

  maskData: {
    editing: false,
    opacity: 0.5,
    maskPrevSrc: '',
    activeLayerIndex: -1,
    dinoPrompt: 'aaa.bbb.ccc',
    layerList: [ ],
  }
});

const getFileExtension = (fileName) => {
  if (fileName === undefined) return '';
  return fileName.substring(fileName.lastIndexOf('.') + 1).toLowerCase();
};

const getFileTypeFromExt = (extension) => {
  if (imageFiles.includes(extension)) {
    return 'image';
  } else if (videoFiles.includes(extension)) {
    return 'video';
  } else if (audioFiles.includes(extension)) {
    return 'audio';
  } else {
    return 'unknown';
  }
};

// See https://stackoverflow.com/a/55292366/21178367
function trim(str, ch) {
    var start = 0, 
        end = str.length;

    while(start < end && str[start] === ch)
        ++start;

    while(end > start && str[end - 1] === ch)
        --end;

    return (start > 0 || end < str.length) ? str.substring(start, end) : str;
};

const updateActiveNode = (filePrev, filePath, nodeType) => {
  explorerData.value.activePrev = filePrev;
  explorerData.value.activePath = filePath;

  if (explorerData.value.maskData.editing) {
    // Quit editor and reset all data
    explorerData.value.maskData.editing = false;
    explorerData.value.maskData.maskPrevSrc = '';
    explorerData.value.maskData.layerList = [ ];
    explorerData.value.maskData.activeLayerIndex = -1;
    // Clear mask graphic
    fileViewRef.value.maskGraphicRef.innerHTML = '';
  }

  var fileType = '???';

  if (nodeType == 'folder') {
    fileType = 'folder';
  } else { // Determine file type by its extension...
    var extension = getFileExtension(filePath);
    fileType = getFileTypeFromExt(extension);
  }

  explorerData.value.activeType = fileType;

  if (fileType != 'image') {
    // Reset image size
    explorerData.value.imageData.width = 0;
    explorerData.value.imageData.height = 0;
    // Reset image data
    explorerData.value.imageData.base64 = '';
  }

  if (useMobileLayout && fileType != 'folder') { // Tapped on a file instead of a folder
    // Hide the file tree and show the file
    popFileTreeShown.value = false;
  }

  console.log(`Active node: ${filePath} (File type: ${fileType}, Preview: ${filePrev})`);
};

const updatePreviewListForFolder = (folderNode) => {
  const newPrevList = [ ];

  folderNode.children.forEach(childNode => {
    const childNodeData = childNode.data;

    if (childNodeData.type == 'file') {
      var extension = getFileExtension(childNodeData.name);
      var fileType = getFileTypeFromExt(extension);

      if (fileType == 'image') { // Image file, add it to preview list
        newPrevList.push({
          prev: `${fileServerHost}/files${childNodeData.filePath}`,
          path: childNodeData.filePath
        });
      } else if (fileType == 'video') { // Video file, add it to preview list
        newPrevList.push({
          prev: FILE_VIDEO_PREV,
          path: childNodeData.filePath
        });
      }
    }
  });

  explorerData.value.prevList = newPrevList;
};

const handleNodeClick = async (fileTree, nodeData, treeNode) => {
  if (nodeData.type == 'folder') {
    updateActiveNode(FOLDER_PREV, nodeData.filePath, 'folder');

    if (!nodeData.children_loaded) {
      // Update folder content
      const pathEncoded = encodeURIComponent(nodeData.filePath);
      fetch(`${fileServerHost}/fileindex?path=${pathEncoded}`)
        .then(response => response.json())
        .then(data => {

          var pathSplit = trim(nodeData.filePath, '/').split('/');
          var updateTargetNodeName = pathSplit.pop(); // Get and remove last segment of the path
          var updateTargetNodeList = explorerData.value.fileIndex;

          // Go down the path
          pathSplit.forEach(indexNodeName => {
            updateTargetNodeList.some(candidateNode => { // Alternative for break, see https://stackoverflow.com/a/2641374/21178367
              var check = candidateNode.name == indexNodeName;
              if (check) {
                updateTargetNodeList = candidateNode.children; // Get first node in path
              }
              return check;
            });
          });

          var updateTargetNode;

          updateTargetNodeList.some(candidateNode => { // Alternative for break, see https://stackoverflow.com/a/2641374/21178367
            var check = candidateNode.name == updateTargetNodeName;
            if (check) {
              updateTargetNode = candidateNode; // Get first node in path
            }
            return check;
          });

          // Convert received data to proper structure (from array to dictionary/object)
          updateTargetNode.children = convertNodeData(nodeData.filePath, data);
          //console.log(updateTargetNode.children);
          fileTree.setData(explorerData.value.fileIndex);

          nodeData.children_loaded = true;
        });
    }
    
  } else if (nodeData.type == 'file') {
    var extension = getFileExtension(nodeData.name);

    if (imageFiles.includes(extension)) {
      updateActiveNode(`${fileServerHost}/files${nodeData.filePath}`, nodeData.filePath, 'file');
    } else {
      updateActiveNode(FILE_UNKNOWN_PREV, nodeData.filePath, 'file');
    }

  } else {
    console.log(`Invalid node type: ${nodeData.type}`);
  }

  if (treeNode.parent !== undefined) {
    // Update preview list...
    updatePreviewListForFolder(treeNode.parent);
  } else {
    // Clear preview list...
    explorerData.value.prevList = [ ];
  }
};

const convertNodeData = (basePath, nodeData) => {
  var converted = [ ];

  for (const [nodeName, node] of Object.entries(nodeData)) { // For each key-value pair
    node.name = nodeName;
    // Display name of nodes can be overriden
    node.nameOverride = node['name_override'];
    // But the path should not
    node.filePath = `${basePath}/${nodeName}`;

    if (node.type == 'folder') {
      if (node.empty) {
        node.children = [ ];
        node.children_loaded = true;
      } else {
        node.children = [ { type: "file", name: "Loading" } ];
        node.children_loaded = false;
      }
    }
    converted.push(node);
  }

  return converted;
};

onMounted(() => {
  window.addEventListener('resize', handleResize);
  // Initialize with files and folders at root folder
  fetch(`${fileServerHost}/fileindex`)
    .then(response => response.json())
    .then(data => {
      // Convert received data to proper structure (from array to dictionary/object)
      explorerData.value.fileIndex = convertNodeData('', data);
    });
  
  // Initialize address for other servers
  fetch(`${fileServerHost}/maskserver`)
    .then(response => response.text())
    .then(h => {
      maskServerHost = h;
      console.log(`Got mask host: ${maskServerHost}`);
    });
  
  fetch(`${fileServerHost}/inpaintserver`)
    .then(response => response.text())
    .then(h => {
      inPaintServerHost = h;
      console.log(`Got inPaint host: ${inPaintServerHost}`);
    });
});

onUnmounted(() => {
  window.removeEventListener('resize', handleResize);
});

// Mask editor related code
const updateMaskGraphicSize = () => {
  var maskGraphic = fileViewRef.value.maskGraphicRef;
  var prevImage = fileViewRef.value.prevImageRef;

  if (maskGraphic == undefined || prevImage == undefined) { // undefined or null
    return;
  }

  maskGraphic.setAttribute('width', prevImage.clientWidth);
  maskGraphic.setAttribute('height', prevImage.clientHeight);
  maskGraphic.setAttribute("viewBox", `0 0 ${prevImage.naturalWidth} ${prevImage.naturalHeight}`);
}

const editMask = () => {
  const filePath = explorerData.value.activePath;

  if (explorerData.value.activeType == 'image') {
    explorerData.value.maskData.editing = true;

    // Update mask graphic size, this svg element object
    // should be present after selecting an image file
    updateMaskGraphicSize();

    // Update main view
    explorerData.value.maskData.maskPrevSrc = '';

    // Update mask for active image
    const pathEncoded = encodeURIComponent(filePath);
    fetch(`${fileServerHost}/savedmaskimg?path=${pathEncoded}`)
      .then(response => response.blob())
      .then(blob => {
        if (blob.size <= 0) return;

        // Read the Blob as DataURL using the FileReader API
        const reader = new FileReader();
        
        reader.onloadend = function() {
          const dataUrl = reader.result;
          // Add saved mask as a layer
          const savedLayer = {
            name: `Saved Layer`,
            type: 'image',
            controls: [ ],
            selectedMaskIndex: 0,
            maskImages: [ dataUrl ]
          };

          // Update mask data
          explorerData.value.maskData.layerList = [ savedLayer ];
          explorerData.value.maskData.activeLayerIndex = 0; // Select this saved mask layer

          // Update main view
          explorerData.value.maskData.maskPrevSrc = savedLayer.maskImages[savedLayer.selectedMaskIndex];
        }

        reader.readAsDataURL(blob);
      });
  } else {
    explorerData.value.maskData.editing = false;
  }
};

const genBoxLayers = () => {
  const maskdata = explorerData.value.maskData;

  if (maskdata.editing) {
    if (!strValue) {

    }
  }
};

const genLayerMask = () => {
  const maskdata = explorerData.value.maskData;

  if (maskdata.editing) {
    if (maskdata.layerList.length > maskdata.activeLayerIndex && maskdata.activeLayerIndex >= 0) {
      
      const activeLayer = maskdata.layerList[maskdata.activeLayerIndex];

      // Request mask generation
      var pointCount = 0;
      var hasBox = false;
      var pointNums = [ ];
      var boxNums = [ ];

      activeLayer.controls.forEach(control => {
        if (control.type == 'point') {
          pointNums.push([ control.x, control.y, control.label ? 1 : 0 ]);

          pointCount += 1;
        } else if (control.type == 'box') {
          if (!hasBox) {
            hasBox = true;

            boxNums.push([ control.minX, control.minY, control.maxX, control.maxY ]);
          } else {
            control.log('A box has already been defined!');
          }
        } else {
          control.log(`Unsupported control type: ${control.type}`);
        }
      });

      var controlFlag = 0;
      if (pointCount > 0) controlFlag |= 1;
      if (hasBox) controlFlag |= 2;

      if (controlFlag == 0) {
        explorerData.value.imageData.messageText = 'Points and/or box prompts are required for segmentation.';

        return;
      }

      const requestData = {
        'image_bytes': explorerData.value.imageData.base64,
        'control_flag': controlFlag
      };

      if (pointCount > 0) {
        requestData.points = pointNums.join(',');
      }

      if (hasBox) {
        requestData.box = boxNums.join(',');
      }

      explorerData.value.imageData.messageText = 'Generating masks...';

      fetch(`${maskServerHost}/generate_masks`, {
          headers: {
            'Accept': 'application/json',
            'Content-Type': 'application/json'
          },
          method: "POST",
          body: JSON.stringify(requestData)
        })
        .then(response => response.json())
        .then(data => {
          //console.log(data.masks);

          const images = data.masks.map(x => 'data:image/png;base64,' + x.bytes);
          const scores = data.masks.map(x => x.score);
          var selectedIndex = 0;
          var highestScore = 0;

          scores.forEach((score, i) => {
            if (score > highestScore) {
              selectedIndex = i;
              highestScore = score;
            }
          });
          
          console.log(`Selected mask candidate #${selectedIndex}`);

          // Update mask data
          activeLayer.maskImages = images;
          activeLayer.selectedMaskIndex = selectedIndex;

          // Update main view
          explorerData.value.maskData.maskPrevSrc = activeLayer.maskImages[activeLayer.selectedMaskIndex];

          // Update message text
          explorerData.value.imageData.messageText = `Generated ${data.masks.length} mask candidate(s). Scores: ${scores.join(' | ')}`;
        });
    }
  }
};

const defocusLayer = () => {
  // Not very elegant but...
  fileViewRef.value.focusMaskLayer(null, -1);
};

const saveMask = () => {
  const srcPath = explorerData.value.activePath;
  const maskDataUrl = explorerData.value.maskData.maskPrevSrc;

  if (!srcPath) {
    explorerData.value.imageData.messageText = 'No file is currently active!';
    return;
  }

  if (!maskDataUrl || !maskDataUrl.startsWith('data:')) {
    explorerData.value.imageData.messageText = 'Mask image data is not available!';
    return;
  }

  const requestData = {
    // Path of the source image, not the mask image
    source_path: srcPath,
    image_base64: maskDataUrl.replace(/^data:(.+);base64,/, "")
  };

  fetch(`${fileServerHost}/updatemaskimg`, {
      headers: {
        'Content-Type': 'application/json'
      },
      method: "POST",
      body: JSON.stringify(requestData)
    })
    .then(() => {
      // Update message text
      explorerData.value.imageData.messageText = 'Mask image saved';
    });
};

const quitMaskEditor = () => {
  explorerData.value.maskData.editing = false;

  // Clear edit data
  explorerData.value.maskData.layerList = [ ];
  explorerData.value.maskData.activeLayerIndex = -1;
  
  // Clear mask graphic
  const maskGraphic = fileViewRef.value.maskGraphicRef;
  maskGraphic.innerHTML = '';
};
</script>

<template>
  <div class="explorer-page">
    <el-container class="explorer-container">

      <!-- use pop container for file tree -->
      <Teleport v-if="useMobileLayout" to="body">
        <PopContainer :show="popFileTreeShown"
          @close="popFileTreeShown = false">
          <template #header>
            <h3>File Tree</h3>
          </template>
          <template #body>
            <CreeperFileTree
              :fileindex="explorerData.fileIndex"
              :fileprops="fileProps"
              :viewheight="windowHeight * 0.8 - 60"
              :activepath="explorerData.activePath"
              @selectTreeNode="handleNodeClick" />
          </template>
        </PopContainer>
      </Teleport>

      <!-- use side menu for file tree -->
      <Transition name="resize"
        @after-enter="updateMaskGraphicSize"
        @after-leave="updateMaskGraphicSize"
        @enter-cancelled="updateMaskGraphicSize"
        @leave-cancelled="updateMaskGraphicSize">
        <el-aside v-if="!useMobileLayout" class="explorer-aside" width="30%">
          <CreeperFileTree
            :fileindex="explorerData.fileIndex"
            :fileprops="fileProps"
            :viewheight="windowHeight - 60"
            :activepath="explorerData.activePath"
            @selectTreeNode="handleNodeClick" />
        </el-aside>
      </Transition>

      <el-main class="explorer-main">
        <CreeperFileView
          ref="file-view-ref"
          :staticroot="staticFileRoot"
          :activeprev="explorerData.activePrev"
          :activepath="explorerData.activePath"
          :activetype="explorerData.activeType"
          :prevlist="explorerData.prevList"
          :imagedata="explorerData.imageData"
          :maskdata="explorerData.maskData"
          @updateActiveFile="updateActiveNode"
          @genBoxLayers="genBoxLayers"
          @genLayerMask="genLayerMask" />
      </el-main>
      
      <div class="main-file-view-ops-bottom">
        <el-button class="main-file-view-button" @click="editMask"
                   v-if="explorerData.activeType == 'image' && !explorerData.maskData.editing">
          Edit Mask
        </el-button>

        <el-button class="main-file-view-button" @click="saveMask"
                   v-if="explorerData.activeType == 'image' && explorerData.maskData.editing && explorerData.maskData.activeLayerIndex < 0">
          Save Mask
        </el-button>

        <el-button class="main-file-view-button" @click="quitMaskEditor"
                   v-if="explorerData.activeType == 'image' && explorerData.maskData.editing && explorerData.maskData.activeLayerIndex < 0">
          Quit Editor
        </el-button>

        <el-button class="main-file-view-button" @click="defocusLayer"
                   v-if="explorerData.activeType == 'image' && explorerData.maskData.editing && explorerData.maskData.activeLayerIndex >= 0">
          Defocus
        </el-button>
        
        <el-button class="main-file-view-button" @click="popFileTreeShown = true"
                   v-if="useMobileLayout && !popFileTreeShown && !explorerData.maskData.editing">
          File Tree
        </el-button>
      </div>

    </el-container>
  </div>
</template>

<style scoped>
h1 {
  text-align: left;
  font-weight: normal;
  font-size: x-large;
  font-style: italic;
}

.explorer-page {
  position: fixed;
  top: 0;
  bottom: 0;
  left: 0;
  right: 0;
}

.explorer-aside {
  margin: 10px 0 10px 10px;
}

.explorer-main {
  margin: 10px 10px 0 10px;
  padding: 0;
}

.explorer-container {
  margin: 5px;
  height: calc(100% - 10px);
}

.main-file-view-ops-top {
  position: fixed;
  right: 40px;
  top: 40px;
  z-index: 100;
  display: grid;
}

.main-file-view-ops-bottom {
  position: fixed;
  right: 40px;
  bottom: 160px;
  z-index: 100;
  display: grid;
}

.main-file-view-button {
  margin: 3px;
  width: 90px;
}

.resize-enter-active,
.resize-leave-active {
  transition: width 0.5s ease;
}

.resize-enter-from,
.resize-leave-to {
  width: 0;
}

</style>