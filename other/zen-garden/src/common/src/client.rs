//! HTTP Client for Zen Garden Component APIs
//! Provides reusable client for querying any Garden component (Moss, Lantern, etc.)

use anyhow::Result;
use reqwest;
use serde::{de::DeserializeOwned, Serialize};
use std::time::Duration;

/// Standard JSON API response wrapper for Garden component APIs
/// Wraps successful data responses with optional suggestions
/// Note: This is for structured JSON APIs, not SSE or raw HTTP responses
#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct GardenApiResponse<T> {
    pub data: T,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub suggestions: Option<Vec<String>>,
}

impl<T> GardenApiResponse<T> {
    pub fn new(data: T) -> Self {
        Self {
            data,
            suggestions: None,
        }
    }

    pub fn with_suggestions(data: T, suggestions: Vec<String>) -> Self {
        Self {
            data,
            suggestions: Some(suggestions),
        }
    }
}

/// HTTP client for interacting with Zen Garden component APIs (Moss, Lantern, etc.)
/// Provides consistent endpoint handling, request building, and response parsing
/// 
/// This is shared infrastructure - any component that needs to query Garden APIs
/// (Rake querying Moss, Lantern querying Moss, future monitoring tools) can use this client.
pub struct GardenHttpClient<'a> {
    client: &'a reqwest::Client,
    endpoint: String,
}

impl<'a> GardenHttpClient<'a> {
    /// Create a new Garden HTTP client for the given endpoint
    /// Automatically trims trailing slashes for consistent URL construction
    /// 
    /// # Example
    /// ```no_run
    /// use reqwest::Client;
    /// use garden_common::client::GardenHttpClient;
    /// 
    /// let client = Client::new();
    /// let garden = GardenHttpClient::new(&client, "http://localhost:7185");
    /// ```
    pub fn new(client: &'a reqwest::Client, endpoint: impl Into<String>) -> Self {
        let endpoint = endpoint.into();
        Self {
            client,
            endpoint: endpoint.trim_end_matches('/').to_string(),
        }
    }

    /// GET request returning typed GardenApiResponse<T>
    /// Automatically applies error_for_status and JSON parsing
    pub async fn get<T>(&self, path: &str) -> Result<GardenApiResponse<T>>
    where
        T: DeserializeOwned,
    {
        let url = self.build_url(path);
        let response = self.client.get(&url).send().await?;
        Ok(response.error_for_status()?.json().await?)
    }

    /// POST request returning typed GardenApiResponse<T>
    /// Automatically applies error_for_status and JSON parsing
    pub async fn post<T, B>(&self, path: &str, body: &B) -> Result<GardenApiResponse<T>>
    where
        T: DeserializeOwned,
        B: Serialize,
    {
        let url = self.build_url(path);
        let response = self.client.post(&url).json(body).send().await?;
        Ok(response.error_for_status()?.json().await?)
    }

    /// GET request returning raw Response for manual status handling
    /// Use this when you need to inspect status codes before parsing
    pub async fn get_raw(&self, path: &str) -> Result<reqwest::Response> {
        let url = self.build_url(path);
        Ok(self.client.get(&url).send().await?)
    }

    /// POST request returning raw Response for manual status handling
    pub async fn post_raw<B>(&self, path: &str, body: &B) -> Result<reqwest::Response>
    where
        B: serde::Serialize,
    {
        let url = self.build_url(path);
        Ok(self.client.post(&url).json(body).send().await?)
    }

    /// POST request without body, returning raw Response
    pub async fn post_empty(&self, path: &str) -> Result<reqwest::Response> {
        let url = self.build_url(path);
        Ok(self.client.post(&url).send().await?)
    }

    /// GET request with timeout, returning typed GardenApiResponse<T>
    pub async fn get_with_timeout<T>(&self, path: &str, timeout: Duration) -> Result<GardenApiResponse<T>>
    where
        T: DeserializeOwned,
    {
        let url = self.build_url(path);
        let response = self.client.get(&url).timeout(timeout).send().await?;
        Ok(response.error_for_status()?.json().await?)
    }

    /// GET request with timeout, returning raw Response
    pub async fn get_raw_with_timeout(&self, path: &str, timeout: Duration) -> Result<reqwest::Response> {
        let url = self.build_url(path);
        Ok(self.client.get(&url).timeout(timeout).send().await?)
    }

    /// Build full URL from path
    fn build_url(&self, path: &str) -> String {
        format!("{}{}", self.endpoint, path)
    }

    /// Get the endpoint this client is configured for
    pub fn endpoint(&self) -> &str {
        &self.endpoint
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_endpoint_normalization() {
        let client = reqwest::Client::new();
        
        // With trailing slash
        let garden = GardenHttpClient::new(&client, "http://localhost:7185/");
        assert_eq!(garden.endpoint(), "http://localhost:7185");
        
        // Without trailing slash
        let garden = GardenHttpClient::new(&client, "http://localhost:7185");
        assert_eq!(garden.endpoint(), "http://localhost:7185");
        
        // Multiple trailing slashes
        let garden = GardenHttpClient::new(&client, "http://localhost:7185///");
        assert_eq!(garden.endpoint(), "http://localhost:7185");
    }

    #[test]
    fn test_url_construction() {
        let client = reqwest::Client::new();
        let garden = GardenHttpClient::new(&client, "http://localhost:7185");
        
        assert_eq!(garden.build_url("/api/v1/services"), "http://localhost:7185/api/v1/services");
        assert_eq!(garden.build_url("/capabilities"), "http://localhost:7185/capabilities");
    }
}
