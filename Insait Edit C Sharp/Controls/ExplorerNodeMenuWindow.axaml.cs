using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp.Controls;

/// <summary>
/// Represents which kind of explorer node the menu is shown for.
/// Each value corresponds to a separate visual "page" inside the window.
/// </summary>
public enum ExplorerNodeMenuPageType
{
	Solution,
	Project,
	File,
	Folder,
	MultiSelection
}

public sealed class ExplorerNodeMenuAction
{
	public ExplorerNodeMenuAction(string section, string title, string? subtitle, Func<Task> executeAsync, bool isDestructive = false)
	{
		Section = section;
		Title = title;
		Subtitle = subtitle;
		ExecuteAsync = executeAsync;
		IsDestructive = isDestructive;
	}

	public string Section { get; }
	public string Title { get; }
	public string? Subtitle { get; }
	public Func<Task> ExecuteAsync { get; }
	public bool IsDestructive { get; }
}

public partial class ExplorerNodeMenuWindow : Window
{
	private readonly IReadOnlyList<ExplorerNodeMenuAction> _actions;

	// ── Section accent colours ───────────────────────────────────
	private static readonly Dictionary<string, string> _sectionColors =
		new(StringComparer.OrdinalIgnoreCase)
		{
			{ "Open",           "#89B4FA" },  // blue
			{ "Add",            "#FFC09F" },  // orange
			{ "Create",         "#FFC09F" },  // orange
			{ "Build",          "#A6E3A1" },  // green
			{ "Run",            "#A6E3A1" },  // green
			{ "Edit",           "#F9E2AF" },  // yellow
			{ "Navigate",       "#74C7EC" },  // cyan
			{ "Source Control", "#DCC4FF" },  // purple
			{ "Dependencies",   "#CBA6F7" },  // light purple
			{ "Solution",       "#9E90B0" },  // muted
			{ "Project",        "#9E90B0" },  // muted
			{ "File",           "#9E90B0" },  // muted
			{ "Folder",         "#9E90B0" },  // muted
			{ "Start",          "#FFC09F" },  // orange
			{ "Recent",         "#89B4FA" },  // blue
		};

	private static string GetSectionColor(string section) =>
		_sectionColors.TryGetValue(section, out var c) ? c : "#9E90B0";

	private static string GetSectionLabel(string section) =>
		section switch
		{
			"Open" => LocalizationService.Get("ExplorerNodeMenu.Section.Open"),
			"Add" => LocalizationService.Get("ExplorerNodeMenu.Section.Add"),
			"Create" => LocalizationService.Get("ExplorerNodeMenu.Section.Create"),
			"Build" => LocalizationService.Get("ExplorerNodeMenu.Section.Build"),
			"Run" => LocalizationService.Get("ExplorerNodeMenu.Section.Run"),
			"Edit" => LocalizationService.Get("ExplorerNodeMenu.Section.Edit"),
			"Navigate" => LocalizationService.Get("ExplorerNodeMenu.Section.Navigate"),
			"Source Control" => LocalizationService.Get("ExplorerNodeMenu.Section.SourceControl"),
			"Dependencies" => LocalizationService.Get("ExplorerNodeMenu.Section.Dependencies"),
			"Solution" => LocalizationService.Get("ExplorerNodeMenu.Section.Solution"),
			"Project" => LocalizationService.Get("ExplorerNodeMenu.Section.Project"),
			"File" => LocalizationService.Get("ExplorerNodeMenu.Section.File"),
			"Folder" => LocalizationService.Get("ExplorerNodeMenu.Section.Folder"),
			"Start" => LocalizationService.Get("ExplorerNodeMenu.Section.Start"),
			"Recent" => LocalizationService.Get("ExplorerNodeMenu.Section.Recent"),
			_ => section
		};

	/// <summary>
	/// Creates the context-menu window for a specific explorer node page type.
	/// </summary>
	public ExplorerNodeMenuWindow(
		string titleIcon,
		string title,
		ExplorerNodeMenuPageType pageType,
		IEnumerable<ExplorerNodeMenuAction> actions,
		string? itemPath = null)
	{
		InitializeComponent();

		_actions = actions.ToList();

		if (this.FindControl<TextBlock>("TitleIcon") is { } titleIconBlock)
			titleIconBlock.Text = titleIcon;

		if (this.FindControl<TextBlock>("TitleText") is { } titleTextBlock)
			titleTextBlock.Text = title;

		this.FindControl<Button>("CloseTitleBtn")!.Click += OnCloseClicked;
		KeyDown += OnWindowKeyDown;
		Deactivated += (_, _) => Close();

		BuildActionButtons();
	}

	private void BuildActionButtons()
	{
		var host = this.FindControl<StackPanel>("ActionHost");
		if (host == null) return;

		host.Children.Clear();

		var groups = _actions.GroupBy(a => a.Section).ToList();
		bool isFirst = true;

		foreach (var group in groups)
		{
			// Separator (thin line) before every section except the first
			if (!isFirst)
			{
				host.Children.Add(new Border
				{
					Height = 1,
					Margin = new Avalonia.Thickness(8, 3, 8, 3),
					Background = new SolidColorBrush(Color.Parse("#25FFFFFF"))
				});
			}
			isFirst = false;

			// Coloured section label
			var sectionColor = GetSectionColor(group.Key);
			host.Children.Add(new TextBlock
			{
				Text = GetSectionLabel(group.Key),
				FontSize = 9,
				FontWeight = FontWeight.SemiBold,
				Foreground = new SolidColorBrush(Color.Parse(sectionColor)),
				Margin = new Avalonia.Thickness(12, 4, 8, 2),
				LetterSpacing = 0.5
			});

			foreach (var action in group)
			{
				var btn = new Button { Tag = action };
				btn.Classes.Add("menu-item");
				if (action.IsDestructive) btn.Classes.Add("destructive");

				var txt = new TextBlock
				{
					Text = action.Title,
					FontSize = 12,
					VerticalAlignment = VerticalAlignment.Center,
					FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI, Arial"),
					TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
				};

				// Show subtitle (if any) as a tooltip on the button
				if (!string.IsNullOrWhiteSpace(action.Subtitle))
					ToolTip.SetTip(btn, action.Subtitle);

				btn.Content = txt;
				btn.Click += OnActionClicked;
				host.Children.Add(btn);
			}
		}
	}

	private async void OnActionClicked(object? sender, RoutedEventArgs e)
	{
		if (sender is not Button { Tag: ExplorerNodeMenuAction action })
			return;

		try
		{
			await action.ExecuteAsync();
		}
		finally
		{
			Close();
		}
	}

	private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

	private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			BeginMoveDrag(e);
	}

	private void OnWindowKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			e.Handled = true;
			Close();
		}
	}
}

