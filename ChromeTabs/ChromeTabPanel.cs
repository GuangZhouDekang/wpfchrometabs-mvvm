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
    ///     xmlns:MyNamespace="clr-namespace:ChromiumTabs"
    ///
    ///
    /// Step 1b) Using this custom control in a XAML file that exists in a different project.
    /// Add this XmlNamespace attribute to the root element of the markup file where it is 
    /// to be used:
    ///
    ///     xmlns:MyNamespace="clr-namespace:ChromiumTabs;assembly=ChromiumTabs"
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
    ///     <MyNamespace:ChromiumTabPanel/>
    ///
    /// </summary>
    [ToolboxItem(false)]
    public class ChromeTabPanel : Panel
    {
        private bool _hideAddButton;
        internal bool draggingWindow;
        internal Size finalSize;
        internal double overlap;
        internal double leftMargin;
        internal double rightMargin;
        internal double maxTabWidth;
        internal double minTabWidth;
        internal double defaultMeasureHeight;
        internal double currentTabWidth;
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

        private DateTime _lastMouseDown;
        private object _lockObject = new object();
        private bool _hasMouseCapture;
        private volatile bool _IsAnimating;
        static ChromeTabPanel()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ChromeTabPanel), new FrameworkPropertyMetadata(typeof(ChromeTabPanel)));
        }

        public ChromeTabPanel()
        {
            this.maxTabWidth = 125.0;
            this.minTabWidth = 40.0;
            this.leftMargin = 0.0;
            this.rightMargin = 25.0;
            this.overlap = 10.0;
            this.defaultMeasureHeight = 30.0;
            ComponentResourceKey key = new ComponentResourceKey(typeof(ChromeTabPanel), "addButtonStyle");
            Style addButtonStyle = (Style)this.FindResource(key);
            this.addButton = new Button { Style = addButtonStyle };
            this.addButtonSize = new Size(20, 12);

        }

        protected override int VisualChildrenCount
        {
            get { return base.VisualChildrenCount + 1; }
        }

        protected override Visual GetVisualChild(int index)
        {
            if (index == this.VisualChildrenCount - 1)
            {
                return ParentTabControl.IsAddButtonVisible ? this.addButton : null;
            }
            else if (index < this.VisualChildrenCount - 1)
            {
                return base.GetVisualChild(index);
            }
            throw new IndexOutOfRangeException("Not enough visual children in the ChromeTabPanel.");
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            Point start = new Point(0, Math.Round(this.finalSize.Height));
            Point end = new Point(this.finalSize.Width, Math.Round(this.finalSize.Height));
            Color penColor = (Color)ColorConverter.ConvertFromString("#FF999999");
            Brush brush = new SolidColorBrush(penColor);
            Pen pen = new Pen(brush, .5);
            dc.DrawLine(pen, start, end);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            this.rightMargin = ParentTabControl.IsAddButtonVisible ? 25 : 0;
            double activeWidth = finalSize.Width - this.leftMargin - this.rightMargin;
            this.currentTabWidth = Math.Min(Math.Max((activeWidth + (this.Children.Count - 1) * overlap) / this.Children.Count, this.minTabWidth), this.maxTabWidth);
            ParentTabControl.SetCanAddTab(this.currentTabWidth > this.minTabWidth);
            if (ParentTabControl.IsAddButtonVisible)
                if (_hideAddButton)
                    this.addButton.Visibility = System.Windows.Visibility.Hidden;
                else
                    this.addButton.Visibility = this.currentTabWidth > this.minTabWidth ? Visibility.Visible : Visibility.Collapsed;
            this.finalSize = finalSize;
            double offset = leftMargin;
            foreach (UIElement element in this.Children)
            {
                double thickness = 0.0;
                ChromeTabItem item = ItemsControl.ContainerFromElement(this.ParentTabControl, element) as ChromeTabItem;
                thickness = item.Margin.Bottom;
                element.Arrange(new Rect(offset, 0, this.currentTabWidth, finalSize.Height - thickness));
                offset += this.currentTabWidth - overlap;
            }
            if (ParentTabControl.IsAddButtonVisible)
            {
                this.addButtonRect = new Rect(new Point(offset + overlap, (finalSize.Height - this.addButtonSize.Height) / 2), this.addButtonSize);
                this.addButton.Arrange(this.addButtonRect);
            }
            return finalSize;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double activeWidth = double.IsPositiveInfinity(availableSize.Width) ? 500 : availableSize.Width - this.leftMargin - this.rightMargin;
            this.currentTabWidth = Math.Min(Math.Max((activeWidth + (this.Children.Count - 1) * overlap) / this.Children.Count, this.minTabWidth), this.maxTabWidth);
            ParentTabControl.SetCanAddTab(this.currentTabWidth > this.minTabWidth);
            if (parent.IsAddButtonVisible)
            {
                if (_hideAddButton)
                    this.addButton.Visibility = System.Windows.Visibility.Hidden;
                else
                    this.addButton.Visibility = this.currentTabWidth > this.minTabWidth ? Visibility.Visible : Visibility.Collapsed;
            }
            double height = double.IsPositiveInfinity(availableSize.Height) ? this.defaultMeasureHeight : availableSize.Height;
            Size resultSize = new Size(0, availableSize.Height);
            foreach (UIElement child in this.Children)
            {
                ChromeTabItem item = ItemsControl.ContainerFromElement(this.ParentTabControl, child) as ChromeTabItem;
                Size tabSize = new Size(this.currentTabWidth, height - item.Margin.Bottom);
                child.Measure(tabSize);
                resultSize.Width += child.DesiredSize.Width - overlap;
            }
            if (ParentTabControl.IsAddButtonVisible)
            {
                this.addButton.Measure(this.addButtonSize);
                resultSize.Width += this.addButtonSize.Width;
            }
            return resultSize;
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            this.SetTabItemsOnTabs();
        }

        protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
        {
            base.OnVisualChildrenChanged(visualAdded, visualRemoved);
            this.SetTabItemsOnTabs();
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            lock (_lockObject)
            {
                if (this.slideIntervals != null)
                {
                    //   Debug.WriteLine("SlideIntervals is not null, returning");
                    return;
                }

                if (ParentTabControl.IsAddButtonVisible)
                {
                    if (this.addButtonRect.Contains(e.GetPosition(this)))
                    {
                        this.addButton.Background = Brushes.DarkGray;
                        this.InvalidateVisual();
                        return;
                    }
                }
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
        internal void StartTabDrag(ChromeTabItem tab = null)
        {
            Point downPoint = MouseUtilities.CorrectGetPosition(this);
            // Debug.WriteLine(string.Format("Picking up tab at position {0}", downPoint), "ChromeTabPanel");
            if (tab != null)
            {
                double index = ((this.currentTabWidth - overlap) * tab.Index) + (this.currentTabWidth / 2);
                this.downPoint = new Point(index, downPoint.Y);
            }
            else
                this.downPoint = downPoint;
            StartTabDrag(downPoint, tab);
        }

        internal void StartTabDrag(Point p, ChromeTabItem tab = null)
        {

            if (tab == null)
            {
                HitTestResult result = VisualTreeHelper.HitTest(this, this.downPoint);
                if (result == null) { return; }
                DependencyObject source = result.VisualHit;
                while (source != null && !this.Children.Contains(source as UIElement))
                {
                    source = VisualTreeHelper.GetParent(source);
                }
                if (source == null)
                {
                    //    Debug.WriteLine("drag source is null, returning", "ChromeTabPanel");
                    return;
                }
                this.draggedTab = source as ChromeTabItem;
            }
            else if (tab != null)
                this.draggedTab = tab;
            else
            {
                //   Debug.WriteLine("No tab to drag found, returning", "ChromeTabPanel");
                return;
            }

            if (this.draggedTab != null)
            {
                if (this.Children.Count > 1 || !ParentTabControl.DragWindowWithOneTab)
                {
                    this.downTabBoundsPoint = MouseUtilities.CorrectGetPosition(this.draggedTab);
                    Canvas.SetZIndex(this.draggedTab, 1000);
                    ParentTabControl.ChangeSelectedItem(this.draggedTab);
                    ProcessMouseMove(new Point(p.X + 0.1, p.Y));
                }
                else if (this.Children.Count == 1)
                {
                    this.draggingWindow = true;
                    if (ParentTabControl.DragWindowWithOneTab)
                    {

                        Window.GetWindow(this).DragMove();
                    }
                }
            }
            this._lastMouseDown = DateTime.UtcNow;
        }

        private void ProcessMouseMove(Point p)
        {
            if (!ParentTabControl.CanMoveTabs)
                return;
            Point nowPoint = p;
            if (ParentTabControl.IsAddButtonVisible)
            {
                if (this.addButtonRect.Contains(nowPoint) && this.addButton.Background != Brushes.White && this.addButton.Background != Brushes.DarkGray)
                {
                    this.addButton.Background = Brushes.White;
                    this.InvalidateVisual();
                }
                else if (!this.addButtonRect.Contains(nowPoint) && this.addButton.Background != null)
                {
                    this.addButton.Background = null;
                    this.InvalidateVisual();
                }
            }
            if (this.draggedTab == null || this.draggingWindow)
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
                        var diff = i - this.slideIndex;
                        var sign = diff == 0 ? 0 : diff / Math.Abs(diff);
                        var bound = Math.Min(1, Math.Abs(diff)) * ((sign * this.currentTabWidth / 3) + ((Math.Abs(diff) < 2) ? 0 : (diff - sign) * (this.currentTabWidth - this.overlap)));
                        this.slideIntervals.Add(bound);
                    }
                    this.slideIntervals.Add(double.PositiveInfinity);
                    this.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (this.CaptureMouse())
                            {
                                _hasMouseCapture = true;
                                //    Debug.WriteLine("Mouse captured", "ChromeTabPanel");
                            }
                        }));
                }
                else if (this.slideIntervals != null)
                {

                    if (insideTabPoint.X > 0 && (nowPoint.X + (this.draggedTab.ActualWidth - insideTabPoint.X)) >= this.ActualWidth)
                    {
                        //  Debug.WriteLine("Skipping, outside width of panel: panel width is: " + this.ActualWidth, "ChromeTabPanel");
                        return;
                    }
                    else if (insideTabPoint.X < this.downTabBoundsPoint.X && (nowPoint.X - insideTabPoint.X) <= 0)
                    {
                        //   Debug.WriteLine("Skipping, margin is below zero", "ChromeTabPanel");
                        return;
                    }

                    this.draggedTab.Margin = margin;
                    if (_IsAnimating)
                    {
                        //   Debug.WriteLine("Is animating, returning", "ChromeTabPanel");
                        return;
                    }
                    this.addButton.Visibility = System.Windows.Visibility.Hidden;
                    _hideAddButton = true;

                    int changed = 0;

                    if (margin.Left < this.slideIntervals[this.slideIndex - 1])
                    {
                        SwapSlideInterval(this.slideIndex - 1);
                        this.slideIndex -= 1;
                        changed = 1;
                    }
                    else if (margin.Left > this.slideIntervals[this.slideIndex + 1])
                    {
                        SwapSlideInterval(this.slideIndex + 1);
                        this.slideIndex += 1;
                        changed = -1;
                    }
                    if (changed != 0)
                    {
                        var rightedOriginalIndex = this.originalIndex + 1;
                        var diff = 1;
                        if (changed > 0 && this.slideIndex >= rightedOriginalIndex)
                        {
                            changed = 0;
                            diff = 0;
                        }
                        else if (changed < 0 && this.slideIndex <= rightedOriginalIndex)
                        {
                            changed = 0;
                            diff = 2;
                        }
                        ChromeTabItem shiftedTab = this.Children[this.slideIndex - diff] as ChromeTabItem;
                        if (!shiftedTab.Equals(this.draggedTab))
                        {
                            var offset = changed * (this.currentTabWidth - this.overlap);
                            //    Debug.WriteLine(string.Format("StickyReanimate start for tab  {0}, left offset is {1}", shiftedTab.Index, offset), "ChromeTabPanel");
                            StickyReanimate(shiftedTab, offset, 0.10);

                        }
                    }
                }
            }
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            base.OnPreviewMouseMove(e);
            ProcessMouseMove(e.GetPosition(this));

            if (this.draggedTab == null)
            {
                //  Debug.WriteLine("Mouse move: Draggedtab is null, returning", "ChromeTabPanel");
                return;
            }
            Window parentWindow = Window.GetWindow(this);
            Point nowPoint = e.GetPosition(parentWindow);
            bool isOutsideWindow = nowPoint.X < 0 || nowPoint.X > parentWindow.ActualWidth || nowPoint.Y < -70 || nowPoint.Y > 100;
            if (isOutsideWindow == true)
            {
                object viewmodel = draggedTab.Content;
                OnTabRelease(e.GetPosition(this), 0.001);//If we set it to 0 the completed event never fires, so we set it to a small decimal.
                this.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        RaiseEvent(new TabDragEventArgs(ChromeTabControl.TabDraggedOutsideBondsEvent, this, viewmodel));
                    }));
                //  Debug.WriteLine("tab item is outside bonds", "ChromeTabPanel");
            }
        }


        private void OnTabRelease(Point p, double animationDuration = 0.10)
        {
            lock (_lockObject)
            {
                this.draggingWindow = false;
                if (ParentTabControl.IsAddButtonVisible)
                {
                    if (this.addButtonRect.Contains(p) && this.addButton.Background == Brushes.DarkGray)
                    {
                        this.addButton.Background = null;
                        this.InvalidateVisual();
                        if (this.addButton.Visibility == Visibility.Visible)
                        {
                            ParentTabControl.AddTab();
                        }
                        return;
                    }

                }
                if (_hasMouseCapture)
                {

                    this.ReleaseMouseCapture();
                    //  Debug.WriteLine("Releasing mouse capture", "ChromeTabPanel");
                    _hasMouseCapture = false;
                    double offset = 0;

                    if (this.slideIndex < this.originalIndex + 1)
                    {
                        offset = this.slideIntervals[this.slideIndex + 1] - 2 * this.currentTabWidth / 3 + this.overlap;
                    }
                    else if (this.slideIndex > this.originalIndex + 1)
                    {
                        offset = this.slideIntervals[this.slideIndex - 1] + 2 * this.currentTabWidth / 3 - this.overlap;
                    }
                    Action completed = () =>
                    {
                        if (this.draggedTab != null)
                        {
                            ParentTabControl.ChangeSelectedItem(this.draggedTab);
                            object vm = this.draggedTab.Content;
                            this.draggedTab.Margin = new Thickness(offset, 0, -offset, 0);
                            //  Debug.WriteLine(string.Format("Margin set in completed for tab {0}: {1}", this.draggedTab.Index, this.draggedTab.Margin), "ChromeTabPanel");
                            this.draggedTab = null;
                            this.captureGuard = 0;
                            ParentTabControl.MoveTab(this.originalIndex, this.slideIndex - 1);
                            this.slideIntervals = null;
                            this.addButton.Visibility = System.Windows.Visibility.Visible;
                            _hideAddButton = false;
                            this.InvalidateVisual();

                        }
                        else
                        {
                            Debug.WriteLine("reanimate completed, but draggedtab is null", "ChromeTabPanel");
                        }
                    };

                    Reanimate(this.draggedTab, offset, animationDuration, completed);
                    //    Debug.WriteLine(string.Format("Reanimated tab {0}, left offset is {1}", this.draggedTab.Index, offset), "ChromeTabPanel");

                }
                else
                {

                    //   Debug.WriteLine("mouse is not captured", "ChromeTabPanel");
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

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonUp(e);

            OnTabRelease(e.GetPosition(this));
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
                    //  Debug.WriteLine(string.Format("StickyReanimate complete, set tab {0} margin to {1}", tab.Index, tab.Margin), "ChromeTabPanel");
                }
                else
                {
                    //  Debug.WriteLine("StickyReanimate stopped because draggedtab is null", "ChromeTabPanel");
                }
            };

            Reanimate(tab, left, duration, completed);
        }

        private void Reanimate(ChromeTabItem tab, double left, double duration, Action completed)
        {
            _IsAnimating = true;
            if (tab == null)
            {
                _IsAnimating = false;
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
                _IsAnimating = false;
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