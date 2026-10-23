using HIDMaestro;

namespace GlassPad.Input;

// One instance per Xbox profile. Only PadService's state lock may update it.
internal sealed class GamepadAxes
{
    private readonly Dictionary<HMAxis, float> _values;
    private readonly HMAxis _leftX, _leftY, _rightX, _rightY, _leftTrigger, _rightTrigger;

    internal GamepadAxes(HMProfile profile)
    {
        var sticks = profile.Sticks;
        var triggers = profile.Triggers;
        if (sticks.Count < 2 || triggers.Count < 2)
            throw new InvalidOperationException($"{profile.Name} needs two sticks and two triggers.");

        (_leftX, _leftY) = (sticks[0].XAxis, sticks[0].YAxis);
        (_rightX, _rightY) = (sticks[1].XAxis, sticks[1].YAxis);
        (_leftTrigger, _rightTrigger) = (triggers[0].Axis, triggers[1].Axis);
        _values = HMGamepadStateHelpers.StandardAxes(profile);
    }

    internal Dictionary<HMAxis, float> Update(
        float leftX, float leftY, float rightX, float rightY, float leftTrigger, float rightTrigger)
    {
        _values[_leftX] = leftX;
        if (_leftY != HMAxis.None) _values[_leftY] = leftY;
        _values[_rightX] = rightX;
        if (_rightY != HMAxis.None) _values[_rightY] = rightY;
        _values[_leftTrigger] = leftTrigger;
        _values[_rightTrigger] = rightTrigger;
        return _values;
    }
}
