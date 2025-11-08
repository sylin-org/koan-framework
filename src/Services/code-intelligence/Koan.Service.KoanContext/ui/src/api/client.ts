import axios, { AxiosError } from 'axios';
import type { ApiError } from './types';

// Base API client configuration
export const apiClient = axios.create({
  baseURL: '/api',
  headers: {
    'Content-Type': 'application/json',
  },
  timeout: 30000, // 30 seconds
});

// Response interceptor for error handling
apiClient.interceptors.response.use(
  (response) => response,
  (error: AxiosError<ApiError>) => {
    if (error.response) {
      // Server responded with error status
      const apiError: ApiError = error.response.data || {
        error: 'An unknown error occurred',
      };

      console.error('API Error:', {
        status: error.response.status,
        error: apiError.error,
        details: apiError.details,
        hint: apiError.hint,
      });

      return Promise.reject(apiError);
    } else if (error.request) {
      // Request made but no response received
      console.error('Network Error:', error.message);
      return Promise.reject({
        error: 'Network error - server not responding',
        details: error.message,
      } as ApiError);
    } else {
      // Error setting up request
      console.error('Request Error:', error.message);
      return Promise.reject({
        error: 'Failed to make request',
        details: error.message,
      } as ApiError);
    }
  }
);

// Request interceptor for logging (development only)
if (import.meta.env.DEV) {
  apiClient.interceptors.request.use(
    (config) => {
      console.log(`API Request: ${config.method?.toUpperCase()} ${config.url}`, config.data);
      return config;
    },
    (error) => {
      console.error('Request Error:', error);
      return Promise.reject(error);
    }
  );
}
