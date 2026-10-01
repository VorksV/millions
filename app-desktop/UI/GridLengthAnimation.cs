using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace VoltrisOptimizer.UI
{
    /// <summary>
    /// Animação de coluna de grade entre dois <see cref="GridLength"/> (pixels).
    /// Usada na transição suave do sidebar entre o modo expandido e o modo rail.
    /// </summary>
    public class GridLengthAnimation : AnimationTimeline
    {
        public static readonly DependencyProperty FromProperty =
            DependencyProperty.Register(nameof(From), typeof(GridLength), typeof(GridLengthAnimation),
                new PropertyMetadata(new GridLength(1.0, GridUnitType.Pixel)));

        public static readonly DependencyProperty ToProperty =
            DependencyProperty.Register(nameof(To), typeof(GridLength), typeof(GridLengthAnimation),
                new PropertyMetadata(new GridLength(1.0, GridUnitType.Pixel)));

        public GridLength From
        {
            get => (GridLength)GetValue(FromProperty);
            set => SetValue(FromProperty, value);
        }

        public GridLength To
        {
            get => (GridLength)GetValue(ToProperty);
            set => SetValue(ToProperty, value);
        }

        public override Type TargetPropertyType => typeof(GridLength);

        public IEasingFunction? EasingFunction { get; set; }

        protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

        public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
        {
            if (animationClock?.CurrentProgress is not double progress)
                return defaultOriginValue;

            var eased = EasingFunction?.Ease(progress) ?? progress;

            var fromValue = From.Value;
            var toValue = To.Value;
            var value = fromValue + (toValue - fromValue) * eased;
            if (value < 0)
                value = 0;

            return new GridLength(value, GridUnitType.Pixel);
        }
    }
}