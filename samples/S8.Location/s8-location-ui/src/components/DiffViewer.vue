<template>
  <div class="font-mono text-sm">
    <span 
      v-for="(part, index) in diffParts" 
      :key="index"
      :class="getDiffClass(part.type)"
    >{{ part.text }}</span>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

interface Props {
  original: string
  corrected: string
}

const props = defineProps<Props>()

interface DiffPart {
  text: string
  type: 'unchanged' | 'removed' | 'added'
}

// Simple diff algorithm for highlighting changes
const diffParts = computed<DiffPart[]>(() => {
  const original = props.original.toLowerCase()
  const corrected = props.corrected.toLowerCase()
  
  if (original === corrected) {
    return [{ text: props.corrected, type: 'unchanged' }]
  }
  
  // Simple word-based diff
  const originalWords = original.split(/\s+/)
  const correctedWords = corrected.split(/\s+/)
  const result: DiffPart[] = []
  
  let i = 0, j = 0
  
  while (i < originalWords.length || j < correctedWords.length) {
    if (i >= originalWords.length) {
      // Remaining words are additions
      result.push({ text: correctedWords[j], type: 'added' })
      j++
    } else if (j >= correctedWords.length) {
      // Remaining words are removals (skip them)
      i++
    } else if (originalWords[i] === correctedWords[j]) {
      // Words match
      result.push({ text: correctedWords[j], type: 'unchanged' })
      i++
      j++
    } else {
      // Look ahead to see if this is a substitution or insertion
      const nextOriginalMatch = originalWords.slice(i + 1).findIndex(w => w === correctedWords[j])
      const nextCorrectedMatch = correctedWords.slice(j + 1).findIndex(w => w === originalWords[i])
      
      if (nextOriginalMatch !== -1 && (nextCorrectedMatch === -1 || nextOriginalMatch < nextCorrectedMatch)) {
        // Original word was removed
        i++
      } else {
        // Word was changed or added
        result.push({ text: correctedWords[j], type: 'added' })
        if (nextCorrectedMatch !== -1) {
          i++ // Also skip the original word
        }
        j++
      }
    }
    
    // Add space between words (except for last word)
    if (j < correctedWords.length && result.length > 0) {
      result.push({ text: ' ', type: 'unchanged' })
    }
  }
  
  return result
})

const getDiffClass = (type: string) => {
  switch (type) {
    case 'added':
      return 'bg-green-200 text-green-800 px-1 rounded'
    case 'removed':
      return 'bg-red-200 text-red-800 px-1 rounded line-through'
    default:
      return 'text-gray-700'
  }
}
</script>