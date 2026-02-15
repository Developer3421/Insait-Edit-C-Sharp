import { EditorTab } from '../types';

interface TabBarProps {
  tabs: EditorTab[];
  activeTabId: string | null;
  onTabSelect: (tabId: string) => void;
  onTabClose: (tabId: string) => void;
}

export default function TabBar({
  tabs,
  activeTabId,
  onTabSelect,
  onTabClose,
}: TabBarProps) {
  const getLanguageIcon = (language: string): string => {
    switch (language) {
      case 'csharp':
        return 'C#';
      case 'javascript':
        return 'JS';
      case 'typescript':
      case 'typescriptreact':
        return 'TS';
      case 'xml':
        return 'XML';
      case 'json':
        return 'JSON';
      case 'html':
        return 'HTML';
      case 'css':
        return 'CSS';
      case 'markdown':
        return 'MD';
      default:
        return '📄';
    }
  };

  const handleTabClick = (tabId: string) => {
    onTabSelect(tabId);
  };

  const handleCloseClick = (e: React.MouseEvent, tabId: string) => {
    e.stopPropagation();
    onTabClose(tabId);
  };

  return (
    <div className="tab-bar">
      {tabs.map((tab) => (
        <button
          key={tab.id}
          className={`tab ${tab.id === activeTabId ? 'active' : ''}`}
          onClick={() => handleTabClick(tab.id)}
        >
          <span className="tab-icon">{getLanguageIcon(tab.language)}</span>
          <span className="tab-name">
            {tab.isDirty ? '● ' : ''}
            {tab.fileName}
          </span>
          <button
            className="tab-close"
            onClick={(e) => handleCloseClick(e, tab.id)}
            title="Close"
          >
            ✕
          </button>
        </button>
      ))}
    </div>
  );
}

