/**
 * SSEClient Component
 * Handles Server-Sent Events connections with automatic reconnection
 */
export class SSEClient {
  constructor(options) {
    this.url = options.url;
    this.onMessage = options.onMessage;
    this.onError = options.onError;
    this.onOpen = options.onOpen;
    this.onClose = options.onClose;
    this.reconnectDelay = options.reconnectDelay || 3000;
    this.maxReconnectAttempts = options.maxReconnectAttempts || 5;

    this.eventSource = null;
    this.reconnectAttempts = 0;
    this.shouldReconnect = true;
  }

  connect() {
    if (this.eventSource) {
      this.disconnect();
    }

    console.log(`[SSEClient] Connecting to ${this.url}`);

    try {
      this.eventSource = new EventSource(this.url);

      this.eventSource.onopen = () => {
        console.log('[SSEClient] Connected');
        this.reconnectAttempts = 0;
        if (this.onOpen) {
          this.onOpen();
        }
      };

      this.eventSource.onmessage = (event) => {
        if (this.onMessage) {
          const data = JSON.parse(event.data);
          this.onMessage(data, event);
        }
      };

      this.eventSource.onerror = (error) => {
        console.error('[SSEClient] Error:', error);

        if (this.eventSource.readyState === EventSource.CLOSED) {
          console.log('[SSEClient] Connection closed');
          this.handleReconnect();
        }

        if (this.onError) {
          this.onError(error);
        }
      };

      // Listen for custom event types
      this.setupEventListeners();

    } catch (error) {
      console.error('[SSEClient] Failed to create EventSource:', error);
      if (this.onError) {
        this.onError(error);
      }
      this.handleReconnect();
    }
  }

  setupEventListeners() {
    const eventTypes = [
      'job-update',
      'progress',
      'status',
      'complete',
      'error',
      'heartbeat',
      'job-removed'
    ];

    eventTypes.forEach(eventType => {
      this.eventSource.addEventListener(eventType, (event) => {
        if (this.onMessage) {
          const data = JSON.parse(event.data);
          this.onMessage(data, event, eventType);
        }
      });
    });
  }

  handleReconnect() {
    if (!this.shouldReconnect) {
      console.log('[SSEClient] Reconnect disabled');
      return;
    }

    if (this.reconnectAttempts >= this.maxReconnectAttempts) {
      console.log('[SSEClient] Max reconnect attempts reached');
      if (this.onClose) {
        this.onClose('max_attempts_reached');
      }
      return;
    }

    this.reconnectAttempts++;
    const delay = this.reconnectDelay * Math.pow(1.5, this.reconnectAttempts - 1);

    console.log(`[SSEClient] Reconnecting in ${delay}ms (attempt ${this.reconnectAttempts}/${this.maxReconnectAttempts})`);

    setTimeout(() => {
      this.connect();
    }, delay);
  }

  disconnect() {
    this.shouldReconnect = false;

    if (this.eventSource) {
      console.log('[SSEClient] Disconnecting');
      this.eventSource.close();
      this.eventSource = null;
    }

    if (this.onClose) {
      this.onClose('manual_disconnect');
    }
  }

  getReadyState() {
    if (!this.eventSource) {
      return EventSource.CLOSED;
    }
    return this.eventSource.readyState;
  }

  isConnected() {
    return this.eventSource && this.eventSource.readyState === EventSource.OPEN;
  }
}
