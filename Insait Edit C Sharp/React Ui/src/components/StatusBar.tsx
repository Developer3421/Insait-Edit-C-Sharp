import { EditorState } from '../types';

interface StatusBarProps {
  state: EditorState;
  language: string;
}

export default function StatusBar({ state, language }: StatusBarProps) {
  const getLanguageDisplayName = (lang: string): string => {
    const names: Record<string, string> = {
      csharp: 'C#',
      javascript: 'JavaScript',
      typescript: 'TypeScript',
      typescriptreact: 'TypeScript React',
      javascriptreact: 'JavaScript React',
      json: 'JSON',
      xml: 'XML',
      html: 'HTML',
      css: 'CSS',
      markdown: 'Markdown',
      plaintext: 'Plain Text',
    };
    return names[lang] || lang;
  };

  return (
    <div className="status-bar">
      <div className="status-bar-left">
        <span className="status-item" title="Branch">
          🔀 main
        </span>
        <span className="status-item" title="Sync">
          ↕ 0 ↓ 0
        </span>
        <span className="status-item" title="Problems">
          ⚠ 0 ✕ 0
        </span>
      </div>
      <div className="status-bar-right">
        <span className="status-item" title="Cursor Position">
          Ln {state.cursorLine}, Col {state.cursorColumn}
        </span>
        <span className="status-item" title="Indentation">
          {state.indentation}
        </span>
        <span className="status-item" title="Encoding">
          {state.encoding}
        </span>
        <span className="status-item" title="Line Ending">
          {state.lineEnding}
        </span>
        <span className="status-item" title="Language">
          {getLanguageDisplayName(language)}
        </span>
        <span className="status-item" title="Notifications">
          🔔
        </span>
      </div>
    </div>
  );
}

