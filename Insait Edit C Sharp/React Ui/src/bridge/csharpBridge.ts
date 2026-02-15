import { CSharpBridgeCallbacks } from '../types';

export function setupCSharpBridge(callbacks: CSharpBridgeCallbacks) {
  // Store callbacks globally for C# to call
  window.csharpBridge = callbacks;

  // Listen for messages from C# WebView2
  if (window.chrome?.webview) {
    window.chrome.webview.addEventListener('message', (event: MessageEvent) => {
      const message = event.data;
      
      switch (message.type) {
        case 'openFile':
          callbacks.onOpenFile(message.filePath, message.content, message.fileName);
          break;
        case 'saveComplete':
          callbacks.onSaveFile();
          break;
        case 'themeChange':
          callbacks.onThemeChange(message.theme);
          break;
        case 'setContent':
          // Handle content update from C#
          break;
        default:
          console.log('Unknown message type:', message.type);
      }
    });
  }
}

export function sendToCSharp(type: string, data?: Record<string, unknown>) {
  if (window.chrome?.webview) {
    window.chrome.webview.postMessage({ type, ...data });
  } else {
    console.log('C# Bridge message:', type, data);
  }
}

export function requestSaveFile(content: string, filePath?: string) {
  sendToCSharp('saveFile', { content, filePath });
}

export function requestOpenFile() {
  sendToCSharp('openFile');
}

export function requestNewFile() {
  sendToCSharp('newFile');
}

export function notifyContentChanged(content: string, filePath?: string) {
  sendToCSharp('contentChanged', { content, filePath });
}

export function notifyEditorReady() {
  sendToCSharp('editorReady');
}

