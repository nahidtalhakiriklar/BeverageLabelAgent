/**
 * Label Preview Component
 * Manages the label preview panel, including rendering, printing, and downloading.
 */
class LabelPreview {
    constructor() {
        this.previewArea = document.getElementById('labelPreviewArea');
        this.previewContent = document.getElementById('labelPreviewContent');
        this.emptyState = document.getElementById('emptyState');
        this.printBtn = document.getElementById('printBtn');
        this.downloadBtn = document.getElementById('downloadBtn');
        this.progressFill = document.getElementById('progressFill');
        this.progressValue = document.getElementById('progressValue');
        this.missingFields = document.getElementById('missingFields');
        this.contradictions = document.getElementById('contradictions');

        this._setupActions();
    }

    /**
     * Sets up print and download button handlers
     */
    _setupActions() {
        this.printBtn.addEventListener('click', () => this._printLabel());
        this.downloadBtn.addEventListener('click', () => this._downloadLabel());
    }

    /**
     * Updates the progress bar and field status
     */
    updateProgress(percentage, missing = [], contradictionsList = []) {
        // Animate progress bar
        this.progressFill.style.width = `${percentage}%`;
        this.progressValue.textContent = `${percentage}%`;

        // Update color based on progress
        if (percentage >= 80) {
            this.progressFill.style.background = 'linear-gradient(135deg, #10b981, #059669)';
        } else if (percentage >= 50) {
            this.progressFill.style.background = 'linear-gradient(135deg, #f59e0b, #d97706)';
        } else {
            this.progressFill.style.background = 'var(--accent-gradient)';
        }

        // Update missing fields
        if (missing.length > 0) {
            this.missingFields.innerHTML = missing
                .map(f => `<span class="field-tag">${f}</span>`)
                .join('');
        } else {
            this.missingFields.innerHTML = '<span class="field-tag" style="border-color: rgba(16,185,129,0.3); background: rgba(16,185,129,0.1); color: #10b981;">✓ All required fields complete</span>';
        }

        // Update contradictions
        if (contradictionsList.length > 0) {
            this.contradictions.innerHTML = contradictionsList
                .map(c => `<div class="contradiction-item">${c}</div>`)
                .join('');
        } else {
            this.contradictions.innerHTML = '';
        }
    }

    /**
     * Renders the generated label HTML in the preview area
     */
    showLabel(labelHtml) {
        if (!labelHtml) return;

        this.emptyState.style.display = 'none';
        this.previewContent.style.display = 'block';
        this.previewContent.innerHTML = labelHtml;
        
        // Enable action buttons
        this.printBtn.disabled = false;
        this.downloadBtn.disabled = false;

        // Show preview panel on mobile
        const previewPanel = document.getElementById('previewPanel');
        if (window.innerWidth <= 900) {
            previewPanel.classList.add('visible');
        }
    }

    /**
     * Prints the label
     */
    _printLabel() {
        window.print();
    }

    /**
     * Downloads the label as a PNG image using canvas
     */
    async _downloadLabel() {
        const labelContainer = this.previewContent.querySelector('.label-container');
        if (!labelContainer) return;

        try {
            // Create a canvas from the label content
            const canvas = document.createElement('canvas');
            const scale = 3; // High DPI
            const rect = labelContainer.getBoundingClientRect();
            canvas.width = rect.width * scale;
            canvas.height = rect.height * scale;

            const ctx = canvas.getContext('2d');
            ctx.scale(scale, scale);

            // Use SVG foreignObject approach for HTML-to-canvas
            const svgData = `
                <svg xmlns="http://www.w3.org/2000/svg" width="${rect.width}" height="${rect.height}">
                    <foreignObject width="100%" height="100%">
                        <div xmlns="http://www.w3.org/1999/xhtml">
                            ${labelContainer.outerHTML}
                        </div>
                    </foreignObject>
                </svg>
            `;

            const img = new Image();
            const svgBlob = new Blob([svgData], { type: 'image/svg+xml;charset=utf-8' });
            const url = URL.createObjectURL(svgBlob);

            img.onload = () => {
                ctx.drawImage(img, 0, 0);
                URL.revokeObjectURL(url);

                // Trigger download
                const link = document.createElement('a');
                link.download = 'beverage-label.png';
                link.href = canvas.toDataURL('image/png');
                link.click();
            };

            img.onerror = () => {
                // Fallback: download as HTML
                URL.revokeObjectURL(url);
                this._downloadAsHtml(labelContainer);
            };

            img.src = url;
        } catch (err) {
            console.error('Download failed:', err);
            // Fallback: download as HTML
            const labelContainer2 = this.previewContent.querySelector('.label-container');
            if (labelContainer2) this._downloadAsHtml(labelContainer2);
        }
    }

    /**
     * Fallback: downloads the label as an HTML file
     */
    _downloadAsHtml(labelElement) {
        const htmlContent = `
<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <title>Beverage Label</title>
    <link href="https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700;800&display=swap" rel="stylesheet">
    <style>
        body { margin: 0; padding: 20px; background: white; display: flex; justify-content: center; }
        ${this._getLabelStyles()}
    </style>
</head>
<body>
    ${labelElement.outerHTML}
</body>
</html>`;

        const blob = new Blob([htmlContent], { type: 'text/html' });
        const link = document.createElement('a');
        link.download = 'beverage-label.html';
        link.href = URL.createObjectURL(blob);
        link.click();
        URL.revokeObjectURL(link.href);
    }

    /**
     * Extracts label-specific CSS styles for the download
     */
    _getLabelStyles() {
        const styleSheets = document.styleSheets;
        let labelStyles = '';
        
        try {
            for (const sheet of styleSheets) {
                try {
                    for (const rule of sheet.cssRules) {
                        if (rule.cssText && rule.cssText.includes('label-')) {
                            labelStyles += rule.cssText + '\n';
                        }
                    }
                } catch (e) {
                    // Cross-origin stylesheet, skip
                }
            }
        } catch (e) {
            console.warn('Could not extract styles:', e);
        }

        return labelStyles;
    }
}
