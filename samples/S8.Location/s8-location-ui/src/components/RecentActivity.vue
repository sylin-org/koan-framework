<template>
  <div class="card">
    <div class="card-header">
      <h2 class="text-lg font-semibold text-gray-900">Recent Activity</h2>
    </div>
    
    <div class="card-body">
      <div class="space-y-3">
        <div 
          v-for="activity in activities" 
          :key="activity.id"
          class="flex items-start space-x-3 p-3 bg-gray-50 rounded-lg"
        >
          <div 
            class="w-2 h-2 rounded-full mt-2 flex-shrink-0"
            :class="getActivityColor(activity.type)"
          ></div>
          <div class="flex-1 min-w-0">
            <p class="text-sm text-gray-900">{{ activity.message }}</p>
            <p class="text-xs text-gray-500 mt-1">{{ activity.timestamp }}</p>
          </div>
        </div>
        
        <div v-if="activities.length === 0" class="text-center py-4 text-gray-500">
          <ClockIcon class="w-8 h-8 mx-auto mb-2 opacity-50" />
          <p class="text-sm">No recent activity</p>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { Clock as ClockIcon } from 'lucide-vue-next'

interface Activity {
  id: string
  type: 'success' | 'warning' | 'info' | 'error'
  message: string
  timestamp: string
}

const activities = ref<Activity[]>([
  {
    id: '1',
    type: 'success',
    message: 'Successfully processed 15 locations from inventory adapter',
    timestamp: '2 minutes ago'
  },
  {
    id: '2',
    type: 'info',
    message: 'AI corrected 3 addresses with high confidence scores',
    timestamp: '5 minutes ago'
  },
  {
    id: '3',
    type: 'warning',
    message: 'Cache hit rate below 80% - consider cache optimization',
    timestamp: '12 minutes ago'
  },
  {
    id: '4',
    type: 'success',
    message: 'Health check passed - all services operational',
    timestamp: '15 minutes ago'
  },
  {
    id: '5',
    type: 'info',
    message: 'New canonical location created for "123 Main St"',
    timestamp: '18 minutes ago'
  }
])

const getActivityColor = (type: string) => {
  switch (type) {
    case 'success': return 'bg-green-400'
    case 'warning': return 'bg-yellow-400'
    case 'error': return 'bg-red-400'
    default: return 'bg-blue-400'
  }
}

onMounted(() => {
  // In a real implementation, this would fetch actual activity logs
  console.log('Recent activity component mounted')
})
</script>