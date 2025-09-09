<template>
  <div class="flex items-center space-x-2">
    <div 
      v-for="(stage, index) in stages" 
      :key="stage.name"
      class="flex items-center"
    >
      <!-- Stage indicator -->
      <div 
        class="w-8 h-8 rounded-full flex items-center justify-center text-xs font-medium border-2 transition-all duration-300"
        :class="getStageClass(stage, index)"
      >
        <CheckIcon v-if="stage.completed" class="w-4 h-4" />
        <LoaderIcon v-else-if="stage.active" class="w-4 h-4 animate-spin" />
        <span v-else>{{ index + 1 }}</span>
      </div>
      
      <!-- Connector arrow -->
      <div 
        v-if="index < stages.length - 1"
        class="w-6 h-0.5 mx-1 transition-colors duration-300"
        :class="stages[index + 1].completed || stages[index + 1].active ? 'bg-primary-400' : 'bg-gray-300'"
      ></div>
    </div>
  </div>
  
  <!-- Stage labels (optional, shown on hover) -->
  <div class="mt-2 text-xs text-gray-500 opacity-0 group-hover:opacity-100 transition-opacity duration-200">
    <div class="flex justify-between space-x-2">
      <span>Park</span>
      <span>Resolve</span>
      <span>Imprint</span>
      <span>Promote</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { Check as CheckIcon, Loader as LoaderIcon } from 'lucide-vue-next'

interface Props {
  status: 'Parked' | 'Processing' | 'Active' | 'Failed'
}

const props = defineProps<Props>()

const stages = computed(() => {
  const baseStages = [
    { name: 'Park', completed: false, active: false },
    { name: 'Resolve', completed: false, active: false },
    { name: 'Imprint', completed: false, active: false },
    { name: 'Promote', completed: false, active: false },
  ]

  switch (props.status) {
    case 'Parked':
      baseStages[0].completed = true
      baseStages[1].active = true
      break
    case 'Processing':
      baseStages[0].completed = true
      baseStages[1].completed = true
      baseStages[2].active = true
      break
    case 'Active':
      baseStages.forEach(stage => stage.completed = true)
      break
    case 'Failed':
      baseStages[0].completed = true
      // Leave others as incomplete to show failure
      break
  }

  return baseStages
})

const getStageClass = (stage: { completed: boolean; active: boolean }, index: number) => {
  if (stage.completed) {
    return 'bg-success-500 border-success-500 text-white'
  } else if (stage.active) {
    return 'bg-primary-500 border-primary-500 text-white'
  } else if (props.status === 'Failed' && index > 0) {
    return 'bg-red-100 border-red-300 text-red-600'
  } else {
    return 'bg-gray-100 border-gray-300 text-gray-500'
  }
}
</script>