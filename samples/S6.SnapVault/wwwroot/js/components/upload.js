/**
 * Upload Modal Component
 * File selection, drag-drop, progress tracking, event selection
 */

export class UploadModal {
  constructor(app) {
    this.app = app;
    this.isOpen = false;
    this.selectedFiles = [];
    this.selectedEventId = null;
    this.uploading = false;
    this.modal = null;
    this.render();
  }

  render() {
    // Create modal HTML
    const modal = document.createElement('div');
    modal.className = 'upload-modal';
    modal.innerHTML = `
      <div class="upload-modal-overlay"></div>
      <div class="upload-modal-content">
        <div class="upload-modal-header">
          <h2>Upload Photos</h2>
          <button class="btn-close-modal" aria-label="Close (Esc)">
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"></line>
              <line x1="6" y1="6" x2="18" y2="18"></line>
            </svg>
          </button>
        </div>

        <div class="upload-modal-body">
          <!-- Event Selection -->
          <div class="upload-section">
            <label class="upload-label">Organization</label>
            <select class="event-select">
              <option value="auto">📅 Auto-organize by date</option>
              <option value="">──────────</option>
            </select>
            <button class="btn-create-event">+ Create New Event</button>
          </div>

          <!-- File Selection -->
          <div class="upload-section">
            <label class="upload-label">Select Photos</label>
            <div class="upload-dropzone">
              <svg class="upload-icon" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                <polyline points="17 8 12 3 7 8"></polyline>
                <line x1="12" y1="3" x2="12" y2="15"></line>
              </svg>
              <p class="dropzone-text">Drag photos here or click to browse</p>
              <p class="dropzone-hint">JPG, PNG, HEIC up to 25MB each</p>
              <input type="file" class="file-input" multiple accept="image/*" hidden />
            </div>
          </div>

          <!-- Selected Files List -->
          <div class="upload-section upload-files-list" style="display: none;">
            <label class="upload-label">Selected Files (<span class="file-count">0</span>)</label>
            <div class="files-container"></div>
          </div>

          <!-- Upload Progress -->
          <div class="upload-section upload-progress" style="display: none;">
            <label class="upload-label">Uploading...</label>
            <div class="progress-container"></div>
          </div>
        </div>

        <div class="upload-modal-footer">
          <button class="btn-secondary btn-cancel">Cancel</button>
          <button class="btn-primary btn-upload" disabled>Upload <span class="upload-count"></span></button>
        </div>
      </div>
    `;

    document.body.appendChild(modal);
    this.modal = modal;

    // Event listeners
    this.setupEventListeners();
  }

  setupEventListeners() {
    const overlay = this.modal.querySelector('.upload-modal-overlay');
    const closeBtn = this.modal.querySelector('.btn-close-modal');
    const cancelBtn = this.modal.querySelector('.btn-cancel');
    const uploadBtn = this.modal.querySelector('.btn-upload');
    const dropzone = this.modal.querySelector('.upload-dropzone');
    const fileInput = this.modal.querySelector('.file-input');
    const eventSelect = this.modal.querySelector('.event-select');
    const createEventBtn = this.modal.querySelector('.btn-create-event');

    // Close handlers
    overlay.addEventListener('click', () => this.close());
    closeBtn.addEventListener('click', () => this.close());
    cancelBtn.addEventListener('click', () => this.close());

    // File selection
    dropzone.addEventListener('click', () => fileInput.click());
    fileInput.addEventListener('change', (e) => this.handleFileSelect(e.target.files));

    // Drag and drop
    dropzone.addEventListener('dragover', (e) => {
      e.preventDefault();
      dropzone.classList.add('dragover');
    });

    dropzone.addEventListener('dragleave', () => {
      dropzone.classList.remove('dragover');
    });

    dropzone.addEventListener('drop', (e) => {
      e.preventDefault();
      dropzone.classList.remove('dragover');
      this.handleFileSelect(e.dataTransfer.files);
    });

    // Event selection
    eventSelect.addEventListener('change', (e) => {
      this.selectedEventId = e.target.value;
      this.updateUploadButton();
    });

    // Create event
    createEventBtn.addEventListener('click', () => this.createEvent());

    // Upload
    uploadBtn.addEventListener('click', () => this.startUpload());

    // Keyboard
    document.addEventListener('keydown', (e) => {
      if (this.isOpen && e.key === 'Escape') {
        this.close();
      }
    });
  }

  async open(preSelectedFiles = null) {
    this.isOpen = true;
    this.selectedFiles = [];
    this.selectedEventId = 'auto'; // Default to auto-organize
    this.modal.classList.add('show');

    // Reset event select to auto
    const eventSelect = this.modal.querySelector('.event-select');
    eventSelect.value = 'auto';

    // Load events
    await this.loadEvents();

    // Handle pre-selected files (from drag-and-drop on main area)
    if (preSelectedFiles) {
      this.handleFileSelect(preSelectedFiles);
    }
  }

  close() {
    this.isOpen = false;
    this.modal.classList.remove('show');
    this.reset();
  }

  async loadEvents() {
    try {
      const events = await this.app.api.get('/api/events');
      const select = this.modal.querySelector('.event-select');

      // Reset options but keep auto and separator
      select.innerHTML = `
        <option value="auto">📅 Auto-organize by date</option>
        <option value="" disabled>──────────</option>
      `;

      // Add user events (excluding auto-generated daily events)
      const userEvents = events.filter(e => e.type !== 6); // EventType.DailyAuto = 6
      userEvents.forEach(event => {
        const option = document.createElement('option');
        option.value = event.id;
        option.textContent = `${event.name} (${event.photoCount || 0} photos)`;
        select.appendChild(option);
      });

      // Set back to auto
      select.value = this.selectedEventId || 'auto';
    } catch (error) {
      console.error('Failed to load events:', error);
      this.app.components.toast.show('Failed to load events', { icon: '⚠️', duration: 3000 });
    }
  }

  handleFileSelect(files) {
    const validFiles = Array.from(files).filter(file => this.isValidFile(file));

    if (validFiles.length === 0) {
      this.app.components.toast.show('No valid image files selected', { icon: '⚠️', duration: 3000 });
      return;
    }

    this.selectedFiles = [...this.selectedFiles, ...validFiles];
    this.renderFilesList();
    this.updateUploadButton();
  }

  isValidFile(file) {
    const validTypes = ['image/jpeg', 'image/png', 'image/heic', 'image/heif'];
    const maxSize = 25 * 1024 * 1024; // 25MB

    if (!validTypes.includes(file.type)) {
      this.app.components.toast.show(`Invalid file type: ${file.name}`, { icon: '⚠️', duration: 3000 });
      return false;
    }

    if (file.size > maxSize) {
      this.app.components.toast.show(`File too large: ${file.name} (max 25MB)`, { icon: '⚠️', duration: 3000 });
      return false;
    }

    return true;
  }

  renderFilesList() {
    const container = this.modal.querySelector('.files-container');
    const section = this.modal.querySelector('.upload-files-list');
    const fileCount = this.modal.querySelector('.file-count');

    if (this.selectedFiles.length === 0) {
      section.style.display = 'none';
      return;
    }

    section.style.display = 'block';
    fileCount.textContent = this.selectedFiles.length;

    container.innerHTML = this.selectedFiles.map((file, index) => `
      <div class="file-item" data-index="${index}">
        <svg class="icon" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
          <circle cx="8.5" cy="8.5" r="1.5"></circle>
          <polyline points="21 15 16 10 5 21"></polyline>
        </svg>
        <span class="file-name">${this.escapeHtml(file.name)}</span>
        <span class="file-size">${this.formatFileSize(file.size)}</span>
        <button class="btn-remove-file" data-index="${index}" aria-label="Remove">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <line x1="18" y1="6" x2="6" y2="18"></line>
            <line x1="6" y1="6" x2="18" y2="18"></line>
          </svg>
        </button>
      </div>
    `).join('');

    // Remove file handlers
    container.querySelectorAll('.btn-remove-file').forEach(btn => {
      btn.addEventListener('click', () => {
        const index = parseInt(btn.dataset.index);
        this.selectedFiles.splice(index, 1);
        this.renderFilesList();
        this.updateUploadButton();
      });
    });
  }

  updateUploadButton() {
    const uploadBtn = this.modal.querySelector('.btn-upload');
    const uploadCount = this.modal.querySelector('.upload-count');

    // Allow upload if files selected and either auto or a specific event is chosen
    const canUpload = this.selectedFiles.length > 0 &&
                      (this.selectedEventId === 'auto' || this.selectedEventId);
    uploadBtn.disabled = !canUpload;

    if (this.selectedFiles.length > 0) {
      uploadCount.textContent = `(${this.selectedFiles.length})`;
    } else {
      uploadCount.textContent = '';
    }
  }

  async startUpload() {
    if (this.selectedFiles.length === 0) return;

    this.uploading = true;
    const progressSection = this.modal.querySelector('.upload-progress');
    const progressContainer = this.modal.querySelector('.progress-container');
    const uploadBtn = this.modal.querySelector('.btn-upload');
    const cancelBtn = this.modal.querySelector('.btn-cancel');

    progressSection.style.display = 'block';
    uploadBtn.disabled = true;
    cancelBtn.textContent = 'Close';

    // Chunk files into batches of 10 to stay under 100MB limit
    const CHUNK_SIZE = 10;
    const totalFiles = this.selectedFiles.length;
    const chunks = [];

    for (let i = 0; i < totalFiles; i += CHUNK_SIZE) {
      chunks.push(this.selectedFiles.slice(i, i + CHUNK_SIZE));
    }

    progressContainer.innerHTML = `
      <div class="chunk-progress">
        Uploading batch <span class="current-chunk">1</span> of <span class="total-chunks">${chunks.length}</span>...
      </div>
    `;

    try {
      const jobIds = [];
      let totalQueued = 0;

      // Upload chunks sequentially
      for (let chunkIndex = 0; chunkIndex < chunks.length; chunkIndex++) {
        const chunk = chunks[chunkIndex];

        // Update progress
        const currentChunkSpan = progressContainer.querySelector('.current-chunk');
        if (currentChunkSpan) {
          currentChunkSpan.textContent = chunkIndex + 1;
        }

        // Create FormData for this chunk
        const formData = new FormData();

        // Only append eventId if not auto-organize
        if (this.selectedEventId && this.selectedEventId !== 'auto') {
          formData.append('eventId', this.selectedEventId);
        }

        chunk.forEach(file => {
          formData.append('files', file);
        });

        // Upload this chunk
        const response = await fetch('/api/photos/upload', {
          method: 'POST',
          body: formData
        });

        if (!response.ok) {
          throw new Error(`Batch ${chunkIndex + 1} upload failed: ${response.statusText}`);
        }

        const uploadResponse = await response.json();
        jobIds.push(uploadResponse.jobId);
        totalQueued += uploadResponse.totalQueued;
      }

      this.app.components.toast.show(`Queued ${totalQueued} photo(s) for processing`, {
        icon: '📤',
        duration: 2000
      });

      // Start process monitor for the first job (they'll all process)
      if (jobIds.length > 0) {
        this.app.components.processMonitor.startJob(jobIds[0], totalQueued);
      }

      // Close modal immediately - processing continues in background
      this.close();

    } catch (error) {
      console.error('Upload failed:', error);
      this.app.components.toast.show('Upload failed', { icon: '⚠️', duration: 5000 });
      this.uploading = false;
      uploadBtn.disabled = false;
      cancelBtn.textContent = 'Cancel';
    }
  }


  async createEvent() {
    const eventName = prompt('Enter event name:');
    if (!eventName) return;

    try {
      const newEvent = {
        name: eventName,
        type: 0, // General
        eventDate: new Date().toISOString()
      };

      const response = await this.app.api.post('/api/events', newEvent);
      await this.loadEvents();

      const select = this.modal.querySelector('.event-select');
      select.value = response.id;
      this.selectedEventId = response.id;
      this.updateUploadButton();

      this.app.components.toast.show(`Event "${eventName}" created`, { icon: '✅', duration: 2000 });
    } catch (error) {
      console.error('Failed to create event:', error);
      this.app.components.toast.show('Failed to create event', { icon: '⚠️', duration: 3000 });
    }
  }

  reset() {
    this.selectedFiles = [];
    this.selectedEventId = 'auto';
    this.uploading = false;

    const fileInput = this.modal.querySelector('.file-input');
    fileInput.value = '';

    const eventSelect = this.modal.querySelector('.event-select');
    eventSelect.value = 'auto';

    this.renderFilesList();
    this.updateUploadButton();

    const progressSection = this.modal.querySelector('.upload-progress');
    progressSection.style.display = 'none';
  }

  formatFileSize(bytes) {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
  }

  escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
  }
}
