export interface EditorTab {
  id: string;
  fileName: string;
  filePath: string;
  language: string;
  content: string;
  isDirty: boolean;
}

export interface EditorState {
  cursorLine: number;
  cursorColumn: number;
  encoding: string;
  lineEnding: string;
  language: string;
  indentation: string;
}

export interface FileTreeItem {
  name: string;
  path: string;
  isDirectory: boolean;
  children?: FileTreeItem[];
  isExpanded?: boolean;
}

export interface DiagnosticItem {
  severity: 'error' | 'warning' | 'info' | 'hint';
  message: string;
  line: number;
  column: number;
  endLine: number;
  endColumn: number;
  source: string;
}

export interface CSharpBridgeCallbacks {
  onOpenFile: (filePath: string, content: string, fileName: string) => void;
  onSaveFile: () => void;
  onThemeChange: (theme: string) => void;
}

// Extend Window interface for WebView2 bridge
declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage: (message: unknown) => void;
        addEventListener: (event: string, handler: (e: MessageEvent) => void) => void;
        removeEventListener: (event: string, handler: (e: MessageEvent) => void) => void;
      };
    };
    csharpBridge?: CSharpBridgeCallbacks;
  }
}

