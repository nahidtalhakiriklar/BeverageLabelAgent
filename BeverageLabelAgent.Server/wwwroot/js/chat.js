/**
 * Chat UI Component
 * Handles rendering of chat messages, markdown parsing, and input management.
 */
class ChatUI {
    constructor() {
        this.messagesContainer = document.getElementById('chatMessages');
        this.messageInput = document.getElementById('messageInput');
        this.sendButton = document.getElementById('sendButton');
        this.typingIndicator = document.getElementById('typingIndicator');
        
        this.onSendMessage = null; // Callback
        this._setupInput();
    }

    /**
     * Sets up input event handlers
     */
    _setupInput() {
        // Auto-resize textarea
        this.messageInput.addEventListener('input', () => {
            this.messageInput.style.height = 'auto';
            this.messageInput.style.height = Math.min(this.messageInput.scrollHeight, 120) + 'px';
        });

        // Enter to send, Shift+Enter for newline
        this.messageInput.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                this._handleSend();
            }
        });

        // Send button click
        this.sendButton.addEventListener('click', () => this._handleSend());
    }

    /**
     * Handles sending a message
     */
    _handleSend() {
        const message = this.messageInput.value.trim();
        if (!message || !this.onSendMessage) return;

        // Add user message to chat
        this.addMessage('user', message);

        // Clear input
        this.messageInput.value = '';
        this.messageInput.style.height = 'auto';

        // Disable input while processing
        this.setInputEnabled(false);

        // Trigger callback
        this.onSendMessage(message);
    }

    /**
     * Adds a message to the chat
     */
    addMessage(role, content, messageType = 'text') {
        const messageEl = document.createElement('div');
        messageEl.className = `message ${role}`;

        const avatar = role === 'user' ? '👤' : '🤖';
        const contentClass = messageType === 'error' ? 'message-content error' : 'message-content';
        
        const time = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

        messageEl.innerHTML = `
            <div class="message-avatar">${avatar}</div>
            <div>
                <div class="${contentClass}">${this._renderMarkdown(content)}</div>
                <div class="message-time">${time}</div>
            </div>
        `;

        this.messagesContainer.appendChild(messageEl);
        this._scrollToBottom();
    }

    /**
     * Simple markdown renderer for chat messages
     */
    _renderMarkdown(text) {
        if (!text) return '';

        let html = text
            // Escape HTML first
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            // Bold
            .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
            // Italic
            .replace(/\*(.+?)\*/g, '<em>$1</em>')
            // Inline code
            .replace(/`(.+?)`/g, '<code>$1</code>')
            // Blockquote
            .replace(/^&gt;\s*(.+)$/gm, '<blockquote>$1</blockquote>')
            // Unordered list items
            .replace(/^[•\-]\s+(.+)$/gm, '<li>$1</li>')
            // Links
            .replace(/\[(.+?)\]\((.+?)\)/g, '<a href="$2" target="_blank" rel="noopener">$1</a>')
            // Line breaks
            .replace(/\n\n/g, '</p><p>')
            .replace(/\n/g, '<br>');

        // Wrap consecutive <li> elements in <ul>
        html = html.replace(/((?:<li>.*?<\/li>\s*)+)/g, '<ul>$1</ul>');

        return `<p>${html}</p>`;
    }

    /**
     * Shows/hides the typing indicator
     */
    showTyping(show) {
        this.typingIndicator.style.display = show ? 'flex' : 'none';
        if (show) this._scrollToBottom();
    }

    /**
     * Enables/disables the message input
     */
    setInputEnabled(enabled) {
        this.messageInput.disabled = !enabled;
        this.sendButton.disabled = !enabled;
        if (enabled) {
            this.messageInput.focus();
        }
    }

    /**
     * Scrolls to the bottom of the chat
     */
    _scrollToBottom() {
        requestAnimationFrame(() => {
            this.messagesContainer.scrollTop = this.messagesContainer.scrollHeight;
        });
    }
}
