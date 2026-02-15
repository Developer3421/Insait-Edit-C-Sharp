interface WelcomeScreenProps {
  onNewFile: () => void;
  onOpenFile: () => void;
}

export default function WelcomeScreen({ onNewFile, onOpenFile }: WelcomeScreenProps) {
  return (
    <div className="welcome-screen">
      <div className="welcome-logo">⚡</div>
      <h1 className="welcome-title">Insait Edit</h1>
      <p className="welcome-subtitle">Modern C# IDE powered by Monaco Editor</p>
      
      <div className="welcome-actions">
        <button className="welcome-action" onClick={onNewFile}>
          <span className="welcome-action-icon">📄</span>
          <span>New File</span>
        </button>
        <button className="welcome-action" onClick={onOpenFile}>
          <span className="welcome-action-icon">📁</span>
          <span>Open File</span>
        </button>
        <button className="welcome-action">
          <span className="welcome-action-icon">📂</span>
          <span>Open Folder</span>
        </button>
        <button className="welcome-action">
          <span className="welcome-action-icon">🔀</span>
          <span>Clone Repository</span>
        </button>
      </div>
      
      <div className="welcome-shortcuts">
        <div className="shortcut">
          <span>Show Commands</span>
          <kbd>Ctrl</kbd><kbd>Shift</kbd><kbd>P</kbd>
        </div>
        <div className="shortcut">
          <span>Quick Open</span>
          <kbd>Ctrl</kbd><kbd>P</kbd>
        </div>
        <div className="shortcut">
          <span>Find in Files</span>
          <kbd>Ctrl</kbd><kbd>Shift</kbd><kbd>F</kbd>
        </div>
        <div className="shortcut">
          <span>Toggle Terminal</span>
          <kbd>Ctrl</kbd><kbd>`</kbd>
        </div>
      </div>
    </div>
  );
}

