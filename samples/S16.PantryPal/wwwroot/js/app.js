// PantryPal Frontend App

let currentPhotoId = null;
let currentDetection = null;
let currentDetections = [];

// Initialize
document.addEventListener('DOMContentLoaded', () => {
    setupUpload();
    loadStats();
});

// Upload setup
function setupUpload() {
    const uploadZone = document.getElementById('uploadZone');
    const fileInput = document.getElementById('fileInput');

    uploadZone.addEventListener('click', () => fileInput.click());

    uploadZone.addEventListener('dragover', (e) => {
        e.preventDefault();
        uploadZone.classList.add('dragging');
    });

    uploadZone.addEventListener('dragleave', () => {
        uploadZone.classList.remove('dragging');
    });

    uploadZone.addEventListener('drop', (e) => {
        e.preventDefault();
        uploadZone.classList.remove('dragging');
        const files = e.dataTransfer.files;
        if (files.length > 0) {
            handleFileSelect(files[0]);
        }
    });

    fileInput.addEventListener('change', (e) => {
        if (e.target.files.length > 0) {
            handleFileSelect(e.target.files[0]);
        }
    });
}

// Handle file selection
async function handleFileSelect(file) {
    const uploadStatus = document.getElementById('uploadStatus');
    uploadStatus.innerHTML = '<div class="loading"></div> Uploading and processing...';

    // Show preview
    const reader = new FileReader();
    reader.onload = (e) => {
        const preview = document.getElementById('preview');
        preview.src = e.target.result;
        document.getElementById('previewContainer').style.display = 'block';
    };
    reader.readAsDataURL(file);

    // Upload to API
    const formData = new FormData();
    formData.append('photo', file);

    try {
        const response = await fetch('/api/pantry/upload', {
            method: 'POST',
            body: formData
        });

        const result = await response.json();

        if (response.ok) {
            currentPhotoId = result.photoId;
            currentDetections = result.detections;

            uploadStatus.innerHTML = `✅ Detected ${result.detections.length} item(s) in ${result.processingTimeMs}ms`;

            // Draw bounding boxes
            drawDetections(result.detections);
        } else {
            uploadStatus.innerHTML = `❌ Error: ${result.error || 'Upload failed'}`;
        }
    } catch (error) {
        uploadStatus.innerHTML = `❌ Error: ${error.message}`;
    }
}

// Draw detection bounding boxes
function drawDetections(detections) {
    const overlay = document.getElementById('detectionOverlay');
    const preview = document.getElementById('preview');

    overlay.innerHTML = '';

    // Wait for image to load
    preview.onload = () => {
        const imgRect = preview.getBoundingClientRect();
        const scaleX = preview.width / imgRect.width;
        const scaleY = preview.height / imgRect.height;

        detections.forEach((detection, index) => {
            const box = detection.boundingBox;
            const topCandidate = detection.candidates[0];

            const boxEl = document.createElement('div');
            boxEl.className = 'bounding-box';
            boxEl.style.left = (box.x / scaleX) + 'px';
            boxEl.style.top = (box.y / scaleY) + 'px';
            boxEl.style.width = (box.width / scaleX) + 'px';
            boxEl.style.height = (box.height / scaleY) + 'px';

            const label = document.createElement('div');
            label.className = 'box-label';
            label.textContent = `${topCandidate.name} ${Math.round(topCandidate.confidence * 100)}%`;

            boxEl.appendChild(label);
            boxEl.addEventListener('click', () => openDetectionModal(detection));

            overlay.appendChild(boxEl);
        });
    };
}

// Open detection editor modal
function openDetectionModal(detection) {
    currentDetection = detection;

    const container = document.getElementById('candidatesContainer');
    container.innerHTML = '';

    detection.candidates.forEach((candidate, index) => {
        const div = document.createElement('div');
        div.className = 'candidate' + (index === 0 ? ' selected' : '');
        div.innerHTML = `
            <strong>${candidate.name}</strong>
            <span class="confidence">${Math.round(candidate.confidence * 100)}%</span>
        `;

        div.addEventListener('click', () => {
            document.querySelectorAll('.candidate').forEach(c => c.classList.remove('selected'));
            div.classList.add('selected');
        });

        container.appendChild(div);
    });

    // Pre-fill user input if parsed data exists
    const userInput = document.getElementById('userInput');
    if (detection.parsedData) {
        const parts = [];
        if (detection.parsedData.quantity) {
            parts.push(`${detection.parsedData.quantity} ${detection.parsedData.unit || ''}`);
        }
        if (detection.parsedData.expiresAt) {
            const date = new Date(detection.parsedData.expiresAt);
            parts.push(`expires ${date.toLocaleDateString()}`);
        }
        userInput.value = parts.join(', ');
    } else {
        userInput.value = '';
    }

    document.getElementById('detectionModal').classList.add('active');
}

// Close modal
function closeModal() {
    document.getElementById('detectionModal').classList.remove('active');
}

// Confirm detection
async function confirmDetection() {
    const selectedCandidate = document.querySelector('.candidate.selected');
    if (!selectedCandidate) {
        alert('Please select an item');
        return;
    }

    const candidateIndex = Array.from(document.querySelectorAll('.candidate')).indexOf(selectedCandidate);
    const userInput = document.getElementById('userInput').value;

    const confirmation = {
        detectionId: currentDetection.id,
        selectedCandidateId: currentDetection.candidates[candidateIndex].id,
        userInput: userInput || null
    };

    try {
        const response = await fetch(`/api/pantry/confirm/${currentPhotoId}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ confirmations: [confirmation] })
        });

        const result = await response.json();

        if (response.ok) {
            alert(`✅ Added ${result.confirmed} item(s) to pantry!`);
            closeModal();
            loadStats(); // Refresh stats
        } else {
            alert(`❌ Error: ${result.error || 'Failed to confirm'}`);
        }
    } catch (error) {
        alert(`❌ Error: ${error.message}`);
    }
}

// Load pantry stats
async function loadStats() {
    try {
        const response = await fetch('/api/pantry/stats');
        const stats = await response.json();

        document.getElementById('totalItems').textContent = stats.totalItems;
        document.getElementById('expiringWeek').textContent = stats.expiringInWeek;
        document.getElementById('expiringMonth').textContent = stats.expiringInMonth;
    } catch (error) {
        console.error('Failed to load stats:', error);
    }
}
