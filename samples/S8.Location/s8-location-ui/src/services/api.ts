import axios from 'axios'

// API Base URL - defaulting to localhost:4914 for S8.Location API
const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:4914/api'

const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
  timeout: 10000,
})

// Request interceptor for debugging
apiClient.interceptors.request.use((config) => {
  console.log(`🔗 API Request: ${config.method?.toUpperCase()} ${config.url}`)
  return config
})

// Response interceptor for error handling
apiClient.interceptors.response.use(
  (response) => {
    console.log(`✅ API Response: ${response.status} ${response.config.url}`)
    return response
  },
  (error) => {
    console.error(`❌ API Error: ${error.response?.status} ${error.config?.url}`, error.response?.data)
    return Promise.reject(error)
  }
)

export interface Location {
  id: string
  source: string
  address: string
  aiCorrectedAddress?: string
  agnosticLocationId?: string
  status: 'Parked' | 'Active' | 'Processing' | 'Failed'
  metadata?: Record<string, any>
  createdAt: string
  updatedAt?: string
  confidence?: number
}

export interface SystemHealth {
  status: 'ok' | 'unhealthy' | 'degraded'
  timestamp: string
  services: {
    name: string
    status: 'healthy' | 'unhealthy' | 'unknown'
    responseTime?: number
  }[]
}

export interface ProcessingStats {
  totalProcessed: number
  successRate: number
  aiCorrectionRate: number
  averageProcessingTime: number
  cacheHitRate: number
}

export const locationApi = {
  // Submit a new location for processing
  async submitLocation(locationData: { source?: string; address: string; metadata?: Record<string, any> }): Promise<Location> {
    const response = await apiClient.post('/locations', { 
      address: locationData.address, 
      externalId: locationData.metadata?.externalId 
    })
    return response.data
  },

  // Get all locations with optional filtering
  async getLocations(params?: { 
    source?: string
    status?: string
    limit?: number
    offset?: number
  }): Promise<Location[]> {
    const queryParams: any = {}
    if (params?.limit) queryParams.size = params.limit
    if (params?.offset) queryParams.page = Math.floor((params.offset || 0) / (params.limit || 50)) + 1
    
    const response = await apiClient.get('/locations', { params: queryParams })
    return response.data
  },

  // Get a specific location by ID
  async getLocation(id: string): Promise<Location> {
    const response = await apiClient.get(`/locations/${id}`)
    return response.data
  },

  // Test address resolution directly
  async resolveAddress(address: string): Promise<{ 
    original: string
    corrected: string
    canonicalId: string
    confidence: number
  }> {
    const response = await apiClient.post('/locations/resolve', { address })
    return response.data
  },

  // Search locations by address
  async searchByAddress(address: string): Promise<Location[]> {
    const response = await apiClient.get('/locations/search', { params: { address } })
    return response.data
  },

  // Get system health status
  async getHealth(): Promise<SystemHealth> {
    const response = await apiClient.get('/health')
    return response.data
  },

  // Get processing statistics
  async getStats(): Promise<ProcessingStats> {
    const response = await apiClient.get('/locations/stats')
    return response.data
  }
}

export default apiClient