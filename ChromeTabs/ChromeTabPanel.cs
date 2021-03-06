﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ComponentModel;
using System.Windows.Media.Animation;
using System.Threading;
using System.Diagnostics;
using System.Xml;
using System.IO;
using System.Windows.Markup;
using ChromeTabs.Utilities;


namespace ChromeTabs
{
    /// <summary>
    /// Follow steps 1a or 1b and then 2 to use this custom control in a XAML file.
    ///
    /// Step 1a) Using this custom control in a XAML file that exists in the current project.
    /// Add this XmlNamespace attribute to the root element of the markup file where it is 
    /// to be used:
    ///
    ///     xmlns:MyNamespace="clr-namespace:ChromeTabs"
    ///
    ///
    /// Step 1b) Using this custom control in a XAML file that exists in a different project.
    /// Add this XmlNamespace attribute to the root element of the markup file where it is 
    /// to be used:
    ///
    ///     xmlns:MyNamespace="clr-namespace:ChromeTabs;assembly=ChromeTabs"
    ///
    /// You will also need to add a project reference from the project where the XAML file lives
    /// to this project and Rebuild to avoid compilation errors:
    ///
    ///     Right click on the target project in the Solution Explorer and
    ///     "Add Reference"->"Projects"->[Browse to and select this project]
    ///
    ///
    /// Step 2)
    /// Go ahead and use your control in the XAML file.
    ///
    ///     <MyNamespace:ChromeTabs/>
    ///
    /// </summary>
    [ToolboxItem(false)]
    public class ChromeTabPanel : Panel
    {
        private const double stickyReanimateDuration = 0.10;
        private const double tabWidthSlidePercent = 0.5;
        private bool hideAddButton;
        private Size finalSize;
        private double leftMargin;
        private double rightMargin;
        private double defaultMeasureHeight;
        private double currentTabWidth;
        private int captureGuard;
        private int originalIndex;
        private int slideIndex;
        private List<double> slideIntervals;
        private ChromeTabItem draggedTab;
        private Point downPoint;
        private Point downTabBoundsPoint;
        private ChromeTabControl parent;
        private Rect addButtonRect;
        private Size addButtonSize;
        private Button addButton;
        private DateTime lastMouseDown;
        private object lockObject = new object();

        private double Overlap
        {
            get
            {
                return ParentTabControl != null ? ParentTabControl.TabOverlap : 10;
            }
        }

        private bool _isAddButtonEnabled;

        public bool IsAddButtonEnabled
        {
            get { return _isAddButtonEnabled; }
            set
            {
                if (_isAddButtonEnabled != value)
                {
                    _isAddButtonEnabled = value;
                    addButton.IsEnabled = value;
                    if (ParentTabControl != null)
                    {
                        if (value == false)
                            addButton.Background = ParentTabControl.AddTabButtonDisabledBrush;
                        else
                            addButton.Background = ParentTabControl.AddTabButtonBrush;
                        InvalidateVisual();
                    }
                }
            }
        }


        private double MinTabWidth
        {
            get
            {
                return parent != null ? parent.MinimumTabWidth : 40;
            }
        }
        private double MaxTabWidth
        {
            get
            {
                return parent != null ? parent.MaximumTabWidth : 125;
            }
        }
        private double PinnedTabWidth
        {
            get
            {
                return parent != null ? parent.PinnedTabWidth : MinTabWidth;
            }
        }
        static ChromeTabPanel()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ChromeTabPanel), new FrameworkPropertyMetadata(typeof(ChromeTabPanel)));
        }

        public ChromeTabPanel()
        {
            this.leftMargin = 0.0;
            this.rightMargin = 25.0;
            this.defaultMeasureHeight = 30.0;
            ComponentResourceKey key = new ComponentResourceKey(typeof(ChromeTabPanel), "addButtonStyle");
            Style addButtonStyle = (Style)this.FindResource(key);
            this.addButton = new Button { Style = addButtonStyle };
            this.addButtonSize = new Size(20, 12);
        }

        internal void SetAddButtonControlTemplate(ControlTemplate template)
        {
            Style style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Button.TemplateProperty, template));
            this.addButton.Style = style;
        }

        protected override int VisualChildrenCount
        {
            get { return base.VisualChildrenCount + 1; }
        }

        protected override Visual GetVisualChild(int index)
        {
            if (index == this.VisualChildrenCount - 1)
            {
                return this.addButton;
            }
            else if (index < this.VisualChildrenCount - 1)
            {
                return base.GetVisualChild(index);
            }
            throw new IndexOutOfRangeException("Not enough visual children in the ChromeTabPanel.");
        }


        protected override Size ArrangeOverride(Size finalSize)
        {
            this.rightMargin = ParentTabControl.IsAddButtonVisible ? 25 : 0;
            this.currentTabWidth = CalculateTabWidth(finalSize);
            ParentTabControl.SetCanAddTab(this.currentTabWidth > this.MinTabWidth);

            if (hideAddButton)
                this.addButton.Visibility = System.Windows.Visibility.Hidden;
            else if (ParentTabControl.IsAddButtonVisible)
                this.addButton.Visibility = this.currentTabWidth > this.MinTabWidth ? Visibility.Visible : Visibility.Collapsed;
            else
                this.addButton.Visibility = System.Windows.Visibility.Collapsed;

            this.finalSize = finalSize;
            double offset = leftMargin;
            foreach (UIElement element in this.Children)
            {
                double thickness = 0.0;
                ChromeTabItem item = ItemsControl.ContainerFromElement(this.ParentTabControl, element) as ChromeTabItem;
                thickness = item.Margin.Bottom;
                double tabWidth = element.DesiredSize.Width;
                element.Arrange(new Rect(offset, 0, tabWidth, finalSize.Height - thickness));
                offset += tabWidth - Overlap;
            }
            if (ParentTabControl.IsAddButtonVisible)
            {
                this.addButtonRect = new Rect(new Point(offset + Overlap, (finalSize.Height - this.addButtonSize.Height) / 2), this.addButtonSize);
                this.addButton.Arrange(this.addButtonRect);
            }
            return finalSize;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            this.currentTabWidth = CalculateTabWidth(availableSize);
            ParentTabControl.SetCanAddTab(this.currentTabWidth > this.MinTabWidth);

            if (hideAddButton)
                this.addButton.Visibility = System.Windows.Visibility.Hidden;
            else if (ParentTabControl.IsAddButtonVisible)
                this.addButton.Visibility = this.currentTabWidth > this.MinTabWidth ? Visibility.Visible : Visibility.Collapsed;
            else
                this.addButton.Visibility = System.Windows.Visibility.Collapsed;

            double height = double.IsPositiveInfinity(availableSize.Height) ? this.defaultMeasureHeight : availableSize.Height;
            Size resultSize = new Size(0, availableSize.Height);
            foreach (UIElement child in this.Children)
            {
                ChromeTabItem item = ItemsControl.ContainerFromElement(this.ParentTabControl, child) as ChromeTabItem;
                Size tabSize = new Size(GetWidthForTabItem(item), height - item.Margin.Bottom);
                child.Measure(tabSize);
                resultSize.Width += child.DesiredSize.Width - Overlap;
            }
            if (ParentTabControl.IsAddButtonVisible)
            {
                this.addButton.Measure(this.addButtonSize);
                resultSize.Width += this.addButtonSize.Width;
            }
            return resultSize;
        }
        private double GetWidthForTabItem(ChromeTabItem tab)
        {
            if (tab.IsPinned)
            {
                return this.PinnedTabWidth;
            }
            return this.currentTabWidth;
        }

        private double CalculateTabWidth(Size availableSize)
        {
            double activeWidth = double.IsPositiveInfinity(availableSize.Width) ? 500 : availableSize.Width - this.leftMargin - this.rightMargin;
            int numberOfPinnedTabs = this.Children.Cast<ChromeTabItem>().Count(x => x.IsPinned);

            double totalPinnedTabsWidth = numberOfPinnedTabs > 0 ? ((numberOfPinnedTabs * this.PinnedTabWidth)) : 0;
            double totalNonPinnedTabsWidth = ((activeWidth) + (this.Children.Count - 1) * Overlap) - totalPinnedTabsWidth;
            return Math.Min(Math.Max(totalNonPinnedTabsWidth / (this.Children.Count - numberOfPinnedTabs), this.MinTabWidth), this.MaxTabWidth);
        }



        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            this.SetTabItemsOnTabs();
            if (Children.Count > 0)
            {
                if (Children[0] is ChromeTabItem)
                    ParentTabControl.ChangeSelectedItem(Children[0] as ChromeTabItem);
            }
            if (ParentTabControl != null && ParentTabControl.AddButtonTemplate != null)
            {
                SetAddButtonControlTemplate(ParentTabControl.AddButtonTemplate);
            }
        }

        protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
        {
            base.OnVisualChildrenChanged(visualAdded, visualRemoved);
            this.SetTabItemsOnTabs();
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            lock (lockObject)
            {
                if (this.slideIntervals != null)
                {
                    return;
                }

                if (this.addButtonRect.Contains(e.GetPosition(this)) && IsAddButtonEnabled)
                {
                    if (ParentTabControl != null)
                    {
                        this.addButton.Background = ParentTabControl.AddTabButtonMouseDownBrush;
                        this.InvalidateVisual();
                    }
                    return;
                }

                //Check if we clicked the close button, and return if we do.
                DependencyObject originalSource = e.OriginalSource as DependencyObject;
                bool isButton = false;
                while (true)
                {
                    if (originalSource != null && originalSource.GetType() != typeof(ChromeTabPanel))
                    {
                        var parent = VisualTreeHelper.GetParent(originalSource);
                        if (parent is Button)
                        {
                            isButton = true;
                            break;
                        }
                        originalSource = parent;
                    }
                    else
                        break;
                }
                if (isButton)
                    return;

                this.downPoint = e.GetPosition(this);
                StartTabDrag(this.downPoint);

            }
        }
        internal void StartTabDrag(ChromeTabItem tab = null, bool isTabGrab = false)
        {
            Point downPoint = MouseUtilities.CorrectGetPosition(this);
            if (tab != null)
            {
                this.UpdateLayout();
                double totalWidth = 0;
                for (int i = 0; i < tab.Index; i++)
                {
                    totalWidth += GetWidthForTabItem(Children[i] as ChromeTabItem) - Overlap;
                }
                double xPos = totalWidth + ((GetWidthForTabItem(tab) / 2));
                this.downPoint = new Point(xPos, downPoint.Y);
            }
            else
                this.downPoint = downPoint;

            StartTabDrag(downPoint, tab, isTabGrab);
        }

        private ChromeTabItem GetTabFromMousePosition(Point mousePoint)
        {
            DependencyObject source = GetVisualItemFromMousePosition(mousePoint);
            while (source != null && !this.Children.Contains(source as UIElement))
            {
                source = VisualTreeHelper.GetParent(source);
            }
            return source as ChromeTabItem;
        }
        private DependencyObject GetVisualItemFromMousePosition(Point mousePoint)
        {
            HitTestResult result = VisualTreeHelper.HitTest(this, mousePoint);
            if (result == null) { return null; }
            DependencyObject source = result.VisualHit;

            if (source == null)
            {
                return null;
            }
            return source;
        }

        internal void StartTabDrag(Point p, ChromeTabItem tab = null, bool isTabGrab = false)
        {
            this.lastMouseDown = DateTime.UtcNow;
            if (tab == null)
            {
                tab = GetTabFromMousePosition(this.downPoint);
            }

            if (tab != null)
                this.draggedTab = tab;
            else
            {
                //The mouse is not over a tab item, so just return.
                return;
            }

            if (this.draggedTab != null)
            {
                if (this.Children.Count == 1
                    && ParentTabControl.DragWindowWithOneTab
                    && Mouse.LeftButton == MouseButtonState.Pressed
                    && !isTabGrab)
                {
                    this.draggedTab = null;
                    Window.GetWindow(this).DragMove();
                }
                else
                {
                    this.downTabBoundsPoint = MouseUtilities.CorrectGetPosition(this.draggedTab);
                    Canvas.SetZIndex(this.draggedTab, 1000);
                    ParentTabControl.ChangeSelectedItem(this.draggedTab);
                    if (isTabGrab)
                    {
                        for (int i = 0; i < this.Children.Count; i++)
                        {
                            ProcessMouseMove(new Point(p.X + 0.1, p.Y));
                        }
                    }
                }
            }
        }

        private void ProcessMouseMove(Point p)
        {
            Point nowPoint = p;
            if (ParentTabControl != null && ParentTabControl.IsAddButtonVisible && IsAddButtonEnabled)
            {
                if (this.addButtonRect.Contains(nowPoint) && IsAddButtonEnabled)
                {
                    this.addButton.Background = ParentTabControl.AddTabButtonMouseOverBrush;
                    this.InvalidateVisual();
                }
                else if (!this.addButtonRect.Contains(nowPoint) && IsAddButtonEnabled)
                {
                    this.addButton.Background = ParentTabControl.AddTabButtonBrush;
                    this.InvalidateVisual();
                }
            }
            if (this.draggedTab == null || !ParentTabControl.CanMoveTabs)
                return;

            Point insideTabPoint = this.TranslatePoint(p, this.draggedTab);
            Thickness margin = new Thickness(nowPoint.X - this.downPoint.X, 0, this.downPoint.X - nowPoint.X, 0);

            if (margin.Left != 0)
            {
                int guardValue = Interlocked.Increment(ref this.captureGuard);
                if (guardValue == 1)
                {
                    this.draggedTab.Margin = margin;

                    //we capture the mouse and start tab movement
                    this.originalIndex = this.draggedTab.Index;
                    this.slideIndex = this.originalIndex + 1;
                    //Add slide intervals, the positions  where the tab slides over the next.
                    this.slideIntervals = new List<double>();
                    this.slideIntervals.Add(double.NegativeInfinity);

                    for (int i = 1; i <= this.Children.Count; i += 1)
                    {
                        var tab = this.Children[i - 1] as ChromeTabItem;
                        var diff = i - this.slideIndex;
                        var sign = diff == 0 ? 0 : diff / Math.Abs(diff);
                        var bound = Math.Min(1, Math.Abs(diff)) * ((sign * GetWidthForTabItem(tab) * tabWidthSlidePercent) + ((Math.Abs(diff) < 2) ? 0 : (diff - sign) * (GetWidthForTabItem(tab) - this.Overlap)));
                        this.slideIntervals.Add(bound);
                    }
                    this.slideIntervals.Add(double.PositiveInfinity);
                    this.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (this.CaptureMouse())
                            {
                                Debug.WriteLine("has mouse capture=true");
                            }
                            else
                                Debug.WriteLine("has mouse capture=false");
                        }));
                }
                else if (this.slideIntervals != null)
                {

                    if (insideTabPoint.X > 0 && (nowPoint.X + (this.draggedTab.ActualWidth - insideTabPoint.X)) >= this.ActualWidth)
                    {
                        return;
                    }
                    else if (insideTabPoint.X < this.downTabBoundsPoint.X && (nowPoint.X - insideTabPoint.X) <= 0)
                    {
                        return;
                    }
                    this.draggedTab.Margin = margin;
                    //We return on small marging changes to avoid the tabs jumping around when quickly clicking between tabs.
                    if (Math.Abs(this.draggedTab.Margin.Left) < 10)
                        return;
                    this.addButton.Visibility = System.Windows.Visibility.Hidden;
                    hideAddButton = true;

                    int changed = 0;
                    int localSlideIndex = this.slideIndex;
                    if (margin.Left < this.slideIntervals[localSlideIndex - 1])
                    {
                        SwapSlideInterval(localSlideIndex - 1);
                        localSlideIndex -= 1;
                        changed = 1;
                    }
                    else if (margin.Left > this.slideIntervals[localSlideIndex + 1])
                    {
                        SwapSlideInterval(localSlideIndex + 1);
                        localSlideIndex += 1;
                        changed = -1;
                    }
                    if (changed != 0)
                    {
                        var rightedOriginalIndex = this.originalIndex + 1;
                        var diff = 1;
                        if (changed > 0 && localSlideIndex >= rightedOriginalIndex)
                        {
                            changed = 0;
                            diff = 0;
                        }
                        else if (changed < 0 && localSlideIndex <= rightedOriginalIndex)
                        {
                            changed = 0;
                            diff = 2;
                        }
                        ChromeTabItem shiftedTab = this.Children[localSlideIndex - diff] as ChromeTabItem;

                        if (!shiftedTab.Equals(this.draggedTab)
                            && ((shiftedTab.IsPinned && draggedTab.IsPinned) || (!shiftedTab.IsPinned && !draggedTab.IsPinned)))
                        {
                            var offset = changed * (GetWidthForTabItem(this.draggedTab) - this.Overlap);
                            StickyReanimate(shiftedTab, offset, stickyReanimateDuration);
                            this.slideIndex = localSlideIndex;
                        }
                    }
                }
            }
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            base.OnPreviewMouseMove(e);

            ProcessMouseMove(e.GetPosition(this));

            if (this.draggedTab == null || DateTime.UtcNow.Subtract(lastMouseDown).TotalMilliseconds < 50)
            {
                return;
            }
            Point nowPoint = e.GetPosition(this);
            bool isOutsideTabPanel = nowPoint.X < 0 - ParentTabControl.TabTearTriggerDistance
                || nowPoint.X > this.ActualWidth + ParentTabControl.TabTearTriggerDistance
                || nowPoint.Y < -(this.ActualHeight)
                || nowPoint.Y > this.ActualHeight + 5 + ParentTabControl.TabTearTriggerDistance;

            if (isOutsideTabPanel == true && Mouse.LeftButton == MouseButtonState.Pressed)
            {
                object viewmodel = draggedTab.Content;
                RaiseEvent(new TabDragEventArgs(ChromeTabControl.TabDraggedOutsideBondsEvent, this, viewmodel, this.PointToScreen(e.GetPosition(this))));
                OnTabRelease(e.GetPosition(this), ParentTabControl.CloseTabWhenDraggedOutsideBonds, 0.01);//If we set it to 0 the completed event never fires, so we set it to a small decimal.
            }
        }


        private void OnTabRelease(Point p, bool closeTabOnRelease, double animationDuration = stickyReanimateDuration)
        {
            lock (lockObject)
            {
                if (ParentTabControl != null && ParentTabControl.IsAddButtonVisible)
                {
                    if (this.addButtonRect.Contains(p) && IsAddButtonEnabled)
                    {
                        this.addButton.Background = ParentTabControl.AddTabButtonBrush;
                        this.InvalidateVisual();
                        if (this.addButton.Visibility == Visibility.Visible)
                        {
                            ParentTabControl.AddTab();
                        }
                        return;
                    }

                }
                if (this.IsMouseCaptured)
                {
                    this.ReleaseMouseCapture();
                    double offset = 0;
                    if (this.slideIntervals != null)
                    {
                        if (this.slideIndex < this.originalIndex + 1)
                        {
                            offset = this.slideIntervals[this.slideIndex + 1] - GetWidthForTabItem(this.draggedTab) * (1 - tabWidthSlidePercent) + this.Overlap;
                        }
                        else if (this.slideIndex > this.originalIndex + 1)
                        {
                            offset = this.slideIntervals[this.slideIndex - 1] + GetWidthForTabItem(this.draggedTab) * (1 - tabWidthSlidePercent) - this.Overlap;
                        }
                    }
                    int localSlideIndex = this.slideIndex;
                    Action completed = () =>
                    {
                        if (this.draggedTab != null)
                        {
                            ParentTabControl.ChangeSelectedItem(this.draggedTab);
                            object vm = this.draggedTab.Content;
                            this.draggedTab.Margin = new Thickness(offset, 0, -offset, 0);
                            this.draggedTab = null;
                            this.captureGuard = 0;
                            ParentTabControl.MoveTab(this.originalIndex, localSlideIndex - 1);
                            this.slideIntervals = null;
                            this.addButton.Visibility = System.Windows.Visibility.Visible;
                            hideAddButton = false;
                            this.InvalidateVisual();
                            if (closeTabOnRelease && ParentTabControl.CloseTabCommand != null)
                            {
                                Debug.WriteLine("sendt close tab command");
                                ParentTabControl.CloseTabCommand.Execute(vm);
                            }
                            if (this.Children.Count > 1)
                            {
                                //this fixes a bug where sometimes tabs got stuck in the wrong position.
                                RealignAllTabs();
                            }
                        }
                    };

                    Reanimate(this.draggedTab, offset, animationDuration, completed);
                }
                else
                {
                    if (this.draggedTab != null)
                    {
                        ParentTabControl.ChangeSelectedItem(this.draggedTab);
                        this.draggedTab.Margin = new Thickness(0);
                    }
                    this.draggedTab = null;
                    this.captureGuard = 0;
                    this.slideIntervals = null;
                }
            }
        }

        private void RealignAllTabs()
        {
            for (int i = 0; i < this.Children.Count; i++)
            {
                var shiftedTab = this.Children[i] as ChromeTabItem;
                var offset = 1 * (GetWidthForTabItem(shiftedTab) - this.Overlap);
                shiftedTab.Margin = new Thickness(0, 0, 0, 0);
            }
        }

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonUp(e);
            OnTabRelease(e.GetPosition(this), false);
        }

        protected override void OnVisualParentChanged(DependencyObject oldParent)
        {
            base.OnVisualParentChanged(oldParent);
            this.parent = null;

        }

        private ChromeTabControl ParentTabControl
        {
            get
            {
                if (this.parent == null)
                {
                    DependencyObject parent = this;
                    while (parent != null && !(parent is ChromeTabControl))
                    {
                        parent = VisualTreeHelper.GetParent(parent);
                    }
                    this.parent = parent as ChromeTabControl;
                }
                return this.parent;
            }
        }
        private UIElement GetTopContainer()
        {
            return Application.Current.MainWindow.Content as UIElement;
        }
        private void StickyReanimate(ChromeTabItem tab, double left, double duration)
        {
            Action completed = () =>
            {
                if (this.draggedTab != null)
                {
                    tab.Margin = new Thickness(left, 0, -left, 0);
                }
            };

            Reanimate(tab, left, duration, completed);
        }

        private void Reanimate(ChromeTabItem tab, double left, double duration, Action completed)
        {
            if (tab == null)
            {
                return;
            }
            Thickness offset = new Thickness(left, 0, -left, 0);
            ThicknessAnimation moveBackAnimation = new ThicknessAnimation(tab.Margin, offset, new Duration(TimeSpan.FromSeconds(duration)));
            Storyboard.SetTarget(moveBackAnimation, tab);
            Storyboard.SetTargetProperty(moveBackAnimation, new PropertyPath(FrameworkElement.MarginProperty));
            Storyboard sb = new Storyboard();
            sb.Children.Add(moveBackAnimation);
            sb.FillBehavior = FillBehavior.Stop;
            sb.AutoReverse = false;
            sb.Completed += (o, ea) =>
            {
                sb.Remove();
                if (completed != null)
                {
                    completed();
                }
            };
            sb.Begin();
        }



        private void SetTabItemsOnTabs()
        {
            for (int i = 0; i < this.Children.Count; i += 1)
            {
                DependencyObject depObj = this.Children[i] as DependencyObject;
                if (depObj == null)
                {
                    continue;
                }
                ChromeTabItem item = ItemsControl.ContainerFromElement(this.ParentTabControl, depObj) as ChromeTabItem;
                if (item != null)
                {
                    KeyboardNavigation.SetTabIndex(item, i);
                }
            }
        }

        private void SwapSlideInterval(int index)
        {
            this.slideIntervals[this.slideIndex] = this.slideIntervals[index];
            this.slideIntervals[index] = 0;
        }
    }
}
