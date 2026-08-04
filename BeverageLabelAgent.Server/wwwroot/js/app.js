/**
 * Main Application
 * Initializes SignalR connection, binds UI components, and manages app state.
 */
(function () {
    'use strict';

    // === Initialize UI Components ===
    const chatUI = new ChatUI();
    const labelPreview = new LabelPreview();

    // === DOM References ===
    const connectionStatus = document.getElementById('connectionStatus');
    const statusDot = connectionStatus.querySelector('.status-dot');
    const statusText = connectionStatus.querySelector('.status-text');
    const llmBadge = document.getElementById('llmBadge');
    const llmText = llmBadge.querySelector('.llm-text');
    const mobilePreviewToggle = document.getElementById('mobilePreviewToggle');
    const previewPanel = document.getElementById('previewPanel');

    // === SignalR Connection ===
    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/chathub')
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Information)
        .build();

    // === Connection State Handlers ===
    function updateConnectionUI(state) {
        statusDot.className = 'status-dot';
        switch (state) {
            case 'connected':
                statusDot.classList.add('connected');
                statusText.textContent = 'Connected';
                chatUI.setInputEnabled(true);
                break;
            case 'reconnecting':
                statusText.textContent = 'Reconnecting...';
                chatUI.setInputEnabled(false);
                break;
            case 'disconnected':
                statusDot.classList.add('disconnected');
                statusText.textContent = 'Disconnected';
                chatUI.setInputEnabled(false);
                break;
            default:
                statusText.textContent = 'Connecting...';
        }
    }

    connection.onreconnecting(() => {
        updateConnectionUI('reconnecting');
    });

    connection.onreconnected(() => {
        updateConnectionUI('connected');
    });

    connection.onclose(() => {
        updateConnectionUI('disconnected');
        // Try to reconnect after a delay
        setTimeout(() => startConnection(), 5000);
    });

    // === SignalR Message Handlers ===
    
    /**
     * Receives agent responses (text + optional label)
     */
    connection.on('ReceiveMessage', (response) => {
        chatUI.showTyping(false);
        chatUI.setInputEnabled(true);

        if (response.message) {
            chatUI.addMessage('assistant', response.message, response.messageType || 'text');
        }

        // Update progress
        if (response.completenessPercentage !== undefined) {
            labelPreview.updateProgress(
                response.completenessPercentage,
                response.missingFields || [],
                response.contradictions || []
            );
        }

        // Show label preview if generated
        if (response.labelHtml) {
            labelPreview.showLabel(response.labelHtml);
        }
    });

    /**
     * Typing indicator
     */
    connection.on('AgentTyping', (isTyping) => {
        chatUI.showTyping(isTyping);
    });

    /**
     * Label state update
     */
    connection.on('LabelStateUpdate', (state) => {
        labelPreview.updateProgress(
            state.completeness,
            state.missingFields,
            state.contradictions
        );
    });

    // === Send Message Handler ===
    chatUI.onSendMessage = async (message) => {
        try {
            await connection.invoke('SendMessage', message);
        } catch (err) {
            console.error('Failed to send message:', err);
            chatUI.showTyping(false);
            chatUI.addMessage('assistant', 'Failed to send message. Please check your connection and try again.', 'error');
            chatUI.setInputEnabled(true);
        }
    };

    // === Mobile Preview Toggle ===
    mobilePreviewToggle.addEventListener('click', () => {
        previewPanel.classList.toggle('visible');
    });

    // Close preview on outside click (mobile)
    previewPanel.addEventListener('click', (e) => {
        if (e.target === previewPanel && window.innerWidth <= 900) {
            previewPanel.classList.remove('visible');
        }
    });

    // === Fetch Config Status ===
    async function fetchConfigStatus() {
        try {
            const res = await fetch('/api/config/status');
            const data = await res.json();
            
            if (data.llmConfigured) {
                llmText.textContent = data.llmProvider;
                llmBadge.style.borderColor = 'rgba(16, 185, 129, 0.3)';
            } else {
                llmText.textContent = data.llmProvider;
                llmBadge.style.borderColor = 'rgba(245, 158, 11, 0.3)';
            }
        } catch (err) {
            llmText.textContent = 'Unknown';
        }
    }

    // === Start Connection ===
    async function startConnection() {
        updateConnectionUI('connecting');
        try {
            await connection.start();
            updateConnectionUI('connected');
            console.log('SignalR connected');
        } catch (err) {
            console.error('SignalR connection failed:', err);
            updateConnectionUI('disconnected');
            setTimeout(() => startConnection(), 5000);
        }
    }

    // === Initialize ===
    fetchConfigStatus();
    startConnection();

})();
