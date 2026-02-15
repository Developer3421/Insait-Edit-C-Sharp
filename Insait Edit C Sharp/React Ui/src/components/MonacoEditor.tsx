import { useRef, useEffect, useCallback } from 'react';
import Editor, { OnMount, OnChange } from '@monaco-editor/react';
import * as monaco from 'monaco-editor';
import { notifyEditorReady } from '../bridge/csharpBridge';

interface MonacoEditorProps {
  content: string;
  language: string;
  onChange: (content: string) => void;
  onCursorChange: (line: number, column: number) => void;
}

export default function MonacoEditor({
  content,
  language,
  onChange,
  onCursorChange,
}: MonacoEditorProps) {
  const editorRef = useRef<monaco.editor.IStandaloneCodeEditor | null>(null);

  const handleEditorMount: OnMount = useCallback((editor, monaco) => {
    editorRef.current = editor;

    // Configure editor options
    editor.updateOptions({
      fontSize: 14,
      fontFamily: "'Cascadia Code', 'Fira Code', 'JetBrains Mono', Consolas, monospace",
      fontLigatures: true,
      minimap: {
        enabled: true,
        scale: 1,
        showSlider: 'mouseover',
      },
      scrollBeyondLastLine: false,
      smoothScrolling: true,
      cursorBlinking: 'smooth',
      cursorSmoothCaretAnimation: 'on',
      renderLineHighlight: 'all',
      lineNumbers: 'on',
      renderWhitespace: 'selection',
      bracketPairColorization: {
        enabled: true,
      },
      guides: {
        bracketPairs: true,
        indentation: true,
      },
      padding: {
        top: 8,
        bottom: 8,
      },
      suggest: {
        showKeywords: true,
        showSnippets: true,
        showClasses: true,
        showFunctions: true,
        showVariables: true,
      },
      quickSuggestions: {
        other: true,
        comments: false,
        strings: true,
      },
      parameterHints: {
        enabled: true,
      },
      folding: true,
      foldingStrategy: 'indentation',
      showFoldingControls: 'mouseover',
      formatOnPaste: true,
      formatOnType: true,
      tabSize: 4,
      insertSpaces: true,
      wordWrap: 'off',
      mouseWheelZoom: true,
    });

    // Register cursor position change listener
    editor.onDidChangeCursorPosition((e) => {
      onCursorChange(e.position.lineNumber, e.position.column);
    });

    // Register keyboard shortcuts
    editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, () => {
      // Trigger save
      if (window.chrome?.webview) {
        window.chrome.webview.postMessage({
          type: 'saveFile',
          content: editor.getValue(),
        });
      }
    });

    editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyP, () => {
      // Show command palette
      editor.trigger('', 'editor.action.quickCommand', null);
    });

    editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyMod.Shift | monaco.KeyCode.KeyP, () => {
      // Show command palette
      editor.trigger('', 'editor.action.quickCommand', null);
    });

    // Define custom C# theme
    monaco.editor.defineTheme('insait-dark', {
      base: 'vs-dark',
      inherit: true,
      rules: [
        { token: 'comment', foreground: '6A9955', fontStyle: 'italic' },
        { token: 'keyword', foreground: 'C586C0' },
        { token: 'keyword.control', foreground: 'C586C0' },
        { token: 'type', foreground: '4EC9B0' },
        { token: 'type.identifier', foreground: '4EC9B0' },
        { token: 'class', foreground: '4EC9B0' },
        { token: 'interface', foreground: 'B8D7A3' },
        { token: 'enum', foreground: 'B8D7A3' },
        { token: 'struct', foreground: '86C691' },
        { token: 'string', foreground: 'CE9178' },
        { token: 'number', foreground: 'B5CEA8' },
        { token: 'variable', foreground: '9CDCFE' },
        { token: 'parameter', foreground: '9CDCFE' },
        { token: 'function', foreground: 'DCDCAA' },
        { token: 'method', foreground: 'DCDCAA' },
        { token: 'property', foreground: '9CDCFE' },
        { token: 'namespace', foreground: '4EC9B0' },
        { token: 'attribute', foreground: '4EC9B0' },
        { token: 'operator', foreground: 'D4D4D4' },
        { token: 'delimiter', foreground: 'D4D4D4' },
        { token: 'delimiter.bracket', foreground: 'FFD700' },
        { token: 'preprocessor', foreground: '808080' },
      ],
      colors: {
        'editor.background': '#1E1E1E',
        'editor.foreground': '#D4D4D4',
        'editorLineNumber.foreground': '#858585',
        'editorLineNumber.activeForeground': '#C6C6C6',
        'editor.lineHighlightBackground': '#2D2D3D',
        'editor.selectionBackground': '#264F78',
        'editor.inactiveSelectionBackground': '#3A3D41',
        'editorCursor.foreground': '#AEAFAD',
        'editorWhitespace.foreground': '#3B3B3B',
        'editorIndentGuide.background': '#404040',
        'editorIndentGuide.activeBackground': '#707070',
        'editor.findMatchBackground': '#515C6A',
        'editor.findMatchHighlightBackground': '#EA5C0055',
        'editorBracketMatch.background': '#0064001A',
        'editorBracketMatch.border': '#888888',
        'minimap.background': '#1E1E1E',
        'minimapSlider.background': '#79797933',
        'minimapSlider.hoverBackground': '#79797966',
        'minimapSlider.activeBackground': '#797979AA',
        'scrollbar.shadow': '#00000000',
        'scrollbarSlider.background': '#79797966',
        'scrollbarSlider.hoverBackground': '#646464B3',
        'scrollbarSlider.activeBackground': '#BFBFBF66',
      },
    });

    // Apply custom theme
    monaco.editor.setTheme('insait-dark');

    // Notify C# that editor is ready
    notifyEditorReady();
  }, [onCursorChange]);

  const handleChange: OnChange = useCallback((value) => {
    if (value !== undefined) {
      onChange(value);
    }
  }, [onChange]);

  // Update content when it changes externally
  useEffect(() => {
    if (editorRef.current && editorRef.current.getValue() !== content) {
      const position = editorRef.current.getPosition();
      editorRef.current.setValue(content);
      if (position) {
        editorRef.current.setPosition(position);
      }
    }
  }, [content]);

  return (
    <div className="monaco-container">
      <Editor
        height="100%"
        language={language}
        value={content}
        theme="insait-dark"
        onMount={handleEditorMount}
        onChange={handleChange}
        loading={
          <div className="loading-overlay">
            <div className="loading-spinner" />
          </div>
        }
        options={{
          automaticLayout: true,
        }}
      />
    </div>
  );
}

