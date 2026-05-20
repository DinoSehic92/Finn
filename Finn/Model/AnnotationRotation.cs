namespace Finn.Model;

internal static class AnnotationRotation
{
    public static double NormalizeDegrees(double degrees)
    {
        degrees %= 360;
        if (degrees <= -180) degrees += 360;
        else if (degrees > 180) degrees -= 360;
        return degrees;
    }

    public static double GetRenderRotation(double createdAtRotation)
        => -NormalizeDegrees(createdAtRotation);
}