using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Insait_Edit_C_Sharp.Controls;

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

	public ExplorerNodeMenuWindow(string titleIcon, string title, IEnumerable<ExplorerNodeMenuAction> actions)
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
		if (host == null)
			return;

		host.Children.Clear();

		foreach (var group in _actions.GroupBy(action => action.Section))
		{
			host.Children.Add(new TextBlock
			{
				Text = group.Key,
				Classes = { "section-label" }
			});

			foreach (var action in group)
			{
				var button = new Button
				{
					Classes = { "action-btn" },
					Tag = action,
					Content = BuildButtonContent(action)
				};

				if (action.IsDestructive)
					button.Classes.Add("destructive");

				button.Click += OnActionClicked;
				host.Children.Add(button);
			}
		}
	}

	private static Control BuildButtonContent(ExplorerNodeMenuAction action)
	{
		var stack = new StackPanel { Spacing = 0 };
		stack.Children.Add(new TextBlock
		{
			Text = action.Title,
			Classes = { "action-title" }
		});

		if (!string.IsNullOrWhiteSpace(action.Subtitle))
		{
			stack.Children.Add(new TextBlock
			{
				Text = action.Subtitle,
				Classes = { "action-subtitle" }
			});
		}

		return stack;
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

	private void OnWindowKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			e.Handled = true;
			Close();
		}
	}
}

