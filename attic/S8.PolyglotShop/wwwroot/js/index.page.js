/**
 * PolyglotShop - Index Page Controller
 * Main page logic for translation interface
 */
(function() {
    'use strict';

    const { S8Api, S8Toasts, S8Utils, S8Const } = window;

    // Page state
    let languages = [];
    let currentTranslation = null;

    /**
     * Initialize page
     */
    async function init() {
        try {
            // Load languages
            languages = await S8Api.getLanguages();
            populateLanguageSelects();

            // Set default target language
            document.getElementById('targetLanguage').value = 'es';

            // Setup event listeners
            setupEventListeners();

            S8Toasts.success('Ready to translate!');
        } catch (error) {
            S8Toasts.error('Failed to load languages: ' + error.message);
        }
    }

    /**
     * Populate language select dropdowns
     */
    function populateLanguageSelects() {
        const sourceSelect = document.getElementById('sourceLanguage');
        const targetSelect = document.getElementById('targetLanguage');

        // Add languages to both selects
        languages.forEach(lang => {
            const option = document.createElement('option');
            option.value = lang.code;
            option.textContent = lang.name;

            sourceSelect.appendChild(option.cloneNode(true));
            targetSelect.appendChild(option.cloneNode(true));
        });
    }

    /**
     * Setup event listeners
     */
    function setupEventListeners() {
        const sourceText = document.getElementById('sourceText');
        const translateBtn = document.getElementById('translateBtn');
        const clearBtn = document.getElementById('clearBtn');
        const swapBtn = document.getElementById('swapLanguages');
        const copyBtn = document.getElementById('copyBtn');

        // Source text input
        sourceText.addEventListener('input', handleSourceTextChange);

        // Translate button
        translateBtn.addEventListener('click', handleTranslate);

        // Clear button
        clearBtn.addEventListener('click', handleClear);

        // Swap languages button
        swapBtn.addEventListener('click', handleSwapLanguages);

        // Copy button
        copyBtn.addEventListener('click', handleCopy);

        // Example buttons
        document.querySelectorAll('.example-btn').forEach(btn => {
            btn.addEventListener('click', handleExampleClick);
        });

        // Enter to translate
        sourceText.addEventListener('keydown', (e) => {
            if (e.ctrlKey && e.key === 'Enter') {
                handleTranslate();
            }
        });

        // Drag and drop file support
        setupFileDragDrop(sourceText);
    }

    /**
     * Setup drag-and-drop file support for text area
     */
    function setupFileDragDrop(element) {
        // Prevent default drag behaviors
        ['dragenter', 'dragover', 'dragleave', 'drop'].forEach(eventName => {
            element.addEventListener(eventName, preventDefaults, false);
        });

        function preventDefaults(e) {
            e.preventDefault();
            e.stopPropagation();
        }

        // Visual feedback on drag
        ['dragenter', 'dragover'].forEach(eventName => {
            element.addEventListener(eventName, () => {
                element.classList.add('drag-over');
            }, false);
        });

        ['dragleave', 'drop'].forEach(eventName => {
            element.addEventListener(eventName, () => {
                element.classList.remove('drag-over');
            }, false);
        });

        // Handle dropped files
        element.addEventListener('drop', handleFileDrop, false);
    }

    /**
     * Handle file drop
     */
    async function handleFileDrop(e) {
        const files = e.dataTransfer.files;

        if (files.length === 0) {
            return;
        }

        const file = files[0];
        const fileName = file.name.toLowerCase();

        // Only accept .md and .txt files
        if (!fileName.endsWith('.md') && !fileName.endsWith('.txt')) {
            S8Toasts.warning('Only .md and .txt files are supported');
            return;
        }

        try {
            // Read file content
            const text = await readFileAsText(file);

            if (!text.trim()) {
                S8Toasts.warning('File is empty');
                return;
            }

            // Set source text
            const sourceText = document.getElementById('sourceText');
            sourceText.value = text;

            // Update character count and enable translate button
            handleSourceTextChange({ target: sourceText });

            S8Toasts.info(`File loaded: ${file.name}`);

            // Auto-translate after a brief delay
            setTimeout(() => {
                handleTranslate();
            }, 300);
        } catch (error) {
            S8Toasts.error('Failed to read file: ' + error.message);
        }
    }

    /**
     * Read file as text
     */
    function readFileAsText(file) {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();

            reader.onload = (e) => {
                resolve(e.target.result);
            };

            reader.onerror = () => {
                reject(new Error('Failed to read file'));
            };

            reader.readAsText(file);
        });
    }

    /**
     * Handle source text change
     */
    function handleSourceTextChange(e) {
        const text = e.target.value;
        const charCount = document.getElementById('sourceCharCount');
        const translateBtn = document.getElementById('translateBtn');

        // Update character count (informational only)
        charCount.textContent = `${text.length} characters`;

        // Enable/disable translate button
        translateBtn.disabled = text.trim().length === 0;
    }

    /**
     * Handle translate action
     */
    async function handleTranslate() {
        const sourceText = document.getElementById('sourceText').value.trim();
        const sourceLanguage = document.getElementById('sourceLanguage').value;
        const targetLanguage = document.getElementById('targetLanguage').value;
        const targetTextArea = document.getElementById('targetText');
        const translationInfo = document.getElementById('translationInfo');
        const copyBtn = document.getElementById('copyBtn');

        if (!sourceText) {
            S8Toasts.error('Please enter text to translate');
            return;
        }

        if (sourceLanguage !== 'auto' && sourceLanguage === targetLanguage) {
            S8Toasts.error('Source and target languages must be different');
            return;
        }

        try {
            S8Utils.setLoading(true);

            const result = await S8Api.translate(sourceText, targetLanguage, sourceLanguage);
            currentTranslation = result;

            // Display translation
            targetTextArea.value = result.translatedText;

            // Show translation info
            const sourceLang = S8Utils.getLanguageName(result.detectedSourceLanguage, languages);
            const targetLang = S8Utils.getLanguageName(result.targetLanguage, languages);
            translationInfo.innerHTML = `
                <i class="fas fa-check-circle" style="color: var(--color-success);"></i>
                Translated from <strong>${sourceLang}</strong> to <strong>${targetLang}</strong>
                ${result.model ? ` • Model: ${result.model}` : ''}
            `;

            // Show copy button
            copyBtn.style.display = 'inline-flex';

            S8Toasts.success('Translation completed!');
        } catch (error) {
            S8Toasts.error('Translation failed: ' + error.message);
            targetTextArea.value = '';
            translationInfo.innerHTML = '';
            copyBtn.style.display = 'none';
        } finally {
            S8Utils.setLoading(false);
        }
    }

    /**
     * Handle clear action
     */
    function handleClear() {
        document.getElementById('sourceText').value = '';
        document.getElementById('targetText').value = '';
        document.getElementById('sourceCharCount').textContent = '0 characters';
        document.getElementById('translationInfo').innerHTML = '';
        document.getElementById('translateBtn').disabled = true;
        document.getElementById('copyBtn').style.display = 'none';
        currentTranslation = null;
    }

    /**
     * Handle swap languages
     */
    function handleSwapLanguages() {
        const sourceSelect = document.getElementById('sourceLanguage');
        const targetSelect = document.getElementById('targetLanguage');

        if (sourceSelect.value === 'auto') {
            S8Toasts.info('Cannot swap from auto-detect');
            return;
        }

        const temp = sourceSelect.value;
        sourceSelect.value = targetSelect.value;
        targetSelect.value = temp;

        // Swap text areas if there's a translation
        if (currentTranslation) {
            const sourceText = document.getElementById('sourceText');
            const targetText = document.getElementById('targetText');
            const temp = sourceText.value;
            sourceText.value = targetText.value;
            targetText.value = '';
            document.getElementById('translationInfo').innerHTML = '';
            document.getElementById('copyBtn').style.display = 'none';

            // Update character count
            handleSourceTextChange({ target: sourceText });
        }
    }

    /**
     * Handle copy action
     */
    async function handleCopy() {
        const targetText = document.getElementById('targetText').value;
        if (!targetText) return;

        const success = await S8Utils.copyToClipboard(targetText);
        if (success) {
            S8Toasts.success('Translation copied to clipboard!');
        } else {
            S8Toasts.error('Failed to copy to clipboard');
        }
    }

    /**
     * Handle example button click
     */
    function handleExampleClick(e) {
        const btn = e.currentTarget;
        const text = btn.dataset.text;
        const target = btn.dataset.target;

        document.getElementById('sourceText').value = text;
        document.getElementById('sourceLanguage').value = 'auto';
        document.getElementById('targetLanguage').value = target;

        // Update character count and enable translate button
        handleSourceTextChange({ target: document.getElementById('sourceText') });

        // Auto-translate
        handleTranslate();
    }

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
