<script setup>
import { ref, onMounted } from 'vue'
import { nextTick } from 'vue';

const props = defineProps([
  'viewheight',
  'fileindex',
  'fileprops',
  'activepath'
]);

const emit = defineEmits([
  'selectTreeNode'
]);

// Get a reference to the virtual tree component
const fileTree = ref(null);

onMounted(() => {
  // Get the node on next tick so that tree data is loaded
  // See https://www.jianshu.com/p/543accac02f4
  nextTick(() => {
    var activeTreeNode = fileTree.value.getNode(props.activepath);
    if (activeTreeNode !== undefined) {
      // console.log(activeTreeNode);
      var nodeToExpand = activeTreeNode.parent;
      while (nodeToExpand !== undefined) {
        fileTree.value.expandNode(nodeToExpand);
        nodeToExpand = nodeToExpand.parent;
      }
    }
  });
});
</script>

<template>
  <el-tree-v2 class="explorer-file-tree" ref="fileTree"
    :height="props.viewheight"
    :data="props.fileindex"
    :props="props.fileprops"
    @node-click="(nodeData, treeNode) => emit('selectTreeNode', fileTree, nodeData, treeNode)">
    <template #default="{ node }">
      <span class="prefix" :class="{ 'is-active': node.data.path == props.activepath,
        'is-folder': node.data.type == 'folder', 'is-file': node.data.type != 'folder' }">
        {{ node.label }}
        <span style="color:green">{{ node.data.nameOverride }}</span>
      </span>
    </template>
  </el-tree-v2>
</template>

<style scoped>
.explorer-file-tree {
  background-color: var(--el-fill-color-lighter);
  border-radius: var(--el-border-radius-base);
}

.is-active {
  color: var(--el-color-primary) !important;
}

.is-folder {
  color: var(--el-text-color-regular);
}

.is-file {
  color: var(--el-text-color-primary);
}
</style>