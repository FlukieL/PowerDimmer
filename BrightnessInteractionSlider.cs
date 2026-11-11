using System;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;

namespace PowerDimmer
{
    // Signals deliberate slider interaction without treating binding updates as input.
    public sealed class BrightnessInteractionSlider : Slider
    {
        public event EventHandler? UserInteraction;

        private void NotifyInteraction() => UserInteraction?.Invoke(this, EventArgs.Empty);

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            NotifyInteraction();
            base.OnPreviewMouseLeftButtonDown(e);
        }

        protected override void OnPreviewTouchDown(TouchEventArgs e)
        {
            NotifyInteraction();
            base.OnPreviewTouchDown(e);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or
                Key.Home or Key.End or Key.PageUp or Key.PageDown)
                NotifyInteraction();
            base.OnPreviewKeyDown(e);
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new InteractionPeer(this);

        private sealed class InteractionPeer : SliderAutomationPeer
        {
            private readonly BrightnessInteractionSlider slider;

            public InteractionPeer(BrightnessInteractionSlider owner) : base(owner) => slider = owner;

            public override object? GetPattern(PatternInterface patternInterface)
            {
                var pattern = base.GetPattern(patternInterface);
                return patternInterface == PatternInterface.RangeValue && pattern is IRangeValueProvider range
                    ? new RangeProvider(slider, range)
                    : pattern;
            }
        }

        private sealed class RangeProvider : IRangeValueProvider
        {
            private readonly BrightnessInteractionSlider slider;
            private readonly IRangeValueProvider inner;

            public RangeProvider(BrightnessInteractionSlider slider, IRangeValueProvider inner)
            {
                this.slider = slider;
                this.inner = inner;
            }

            public bool IsReadOnly => inner.IsReadOnly;
            public double LargeChange => inner.LargeChange;
            public double Maximum => inner.Maximum;
            public double Minimum => inner.Minimum;
            public double SmallChange => inner.SmallChange;
            public double Value => inner.Value;

            public void SetValue(double value)
            {
                inner.SetValue(value);
                slider.NotifyInteraction();
            }
        }
    }
}
