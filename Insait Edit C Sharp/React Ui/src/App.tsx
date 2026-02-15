import { useState, useCallback, useEffect } from 'react'
import MonacoEditor from './components/MonacoEditor'
import TabBar from './components/TabBar'
import Breadcrumb from './components/Breadcrumb'
import StatusBar from './components/StatusBar'
import WelcomeScreen from './components/WelcomeScreen'
import { EditorTab, EditorState } from './types'
import { setupCSharpBridge } from './bridge/csharpBridge'

// Sample initial content
const sampleCode = `using System;
using Avalonia;
using Avalonia.Controls;

namespace Insait_Edit_C_Sharp;

public class Program
{
    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Builds the Avalonia application configuration.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
`;

function App() {
  const [tabs, setTabs] = useState<EditorTab[]>([
    {
      id: '1',
      fileName: 'Program.cs',
      filePath: '/Insait Edit C Sharp/Program.cs',
      language: 'csharp',
      content: sampleCode,
      isDirty: false,
    }
  ]);
  const [activeTabId, setActiveTabId] = useState<string | null>('1');
  const [editorState, setEditorState] = useState<EditorState>({
    cursorLine: 1,
    cursorColumn: 1,
    encoding: 'UTF-8',
    lineEnding: 'LF',
    language: 'C#',
    indentation: 'Spaces: 4',
  });

  const activeTab = tabs.find(tab => tab.id === activeTabId);

  // Setup C# bridge for communication with Avalonia
  useEffect(() => {
    setupCSharpBridge({
      onOpenFile: (filePath: string, content: string, fileName: string) => {
        const newTab: EditorTab = {
          id: Date.now().toString(),
          fileName,
          filePath,
          language: getLanguageFromFileName(fileName),
          content,
          isDirty: false,
        };
        setTabs(prev => [...prev, newTab]);
        setActiveTabId(newTab.id);
      },
      onSaveFile: () => {
        if (activeTab) {
          setTabs(prev => prev.map(tab => 
            tab.id === activeTab.id ? { ...tab, isDirty: false } : tab
          ));
        }
      },
      onThemeChange: (_theme: string) => {
        // Monaco theme will be updated
      }
    });
  }, [activeTab]);

  const getLanguageFromFileName = (fileName: string): string => {
    const ext = fileName.split('.').pop()?.toLowerCase();
    switch (ext) {
      case 'cs': return 'csharp';
      case 'js': return 'javascript';
      case 'ts': return 'typescript';
      case 'tsx': return 'typescriptreact';
      case 'jsx': return 'javascriptreact';
      case 'json': return 'json';
      case 'xml':
      case 'axaml':
      case 'xaml': return 'xml';
      case 'html': return 'html';
      case 'css': return 'css';
      case 'md': return 'markdown';
      default: return 'plaintext';
    }
  };

  const handleTabClose = useCallback((tabId: string) => {
    setTabs(prev => {
      const newTabs = prev.filter(tab => tab.id !== tabId);
      if (activeTabId === tabId && newTabs.length > 0) {
        setActiveTabId(newTabs[newTabs.length - 1].id);
      } else if (newTabs.length === 0) {
        setActiveTabId(null);
      }
      return newTabs;
    });
  }, [activeTabId]);

  const handleTabSelect = useCallback((tabId: string) => {
    setActiveTabId(tabId);
  }, []);

  const handleContentChange = useCallback((content: string) => {
    if (activeTab) {
      setTabs(prev => prev.map(tab =>
        tab.id === activeTab.id
          ? { ...tab, content, isDirty: tab.content !== content }
          : tab
      ));
    }
  }, [activeTab]);

  const handleCursorChange = useCallback((line: number, column: number) => {
    setEditorState(prev => ({
      ...prev,
      cursorLine: line,
      cursorColumn: column,
    }));
  }, []);

  const handleNewFile = useCallback(() => {
    const newTab: EditorTab = {
      id: Date.now().toString(),
      fileName: 'Untitled.cs',
      filePath: '',
      language: 'csharp',
      content: '// New file\n',
      isDirty: true,
    };
    setTabs(prev => [...prev, newTab]);
    setActiveTabId(newTab.id);
  }, []);

  const handleOpenFile = useCallback(() => {
    // Send message to C# to open file dialog
    if (window.chrome?.webview) {
      window.chrome.webview.postMessage({ type: 'openFile' });
    }
  }, []);

  return (
    <div className="editor-wrapper">
      {tabs.length > 0 && (
        <>
          <TabBar
            tabs={tabs}
            activeTabId={activeTabId}
            onTabSelect={handleTabSelect}
            onTabClose={handleTabClose}
          />
          {activeTab && (
            <>
              <Breadcrumb filePath={activeTab.filePath} />
              <div className="editor-area">
                <MonacoEditor
                  content={activeTab.content}
                  language={activeTab.language}
                  onChange={handleContentChange}
                  onCursorChange={handleCursorChange}
                />
              </div>
            </>
          )}
        </>
      )}
      
      {tabs.length === 0 && (
        <WelcomeScreen
          onNewFile={handleNewFile}
          onOpenFile={handleOpenFile}
        />
      )}
      
      <StatusBar
        state={editorState}
        language={activeTab?.language || 'plaintext'}
      />
    </div>
  );
}

export default App

