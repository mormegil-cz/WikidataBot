using System;

namespace TestConsole;

public static class Epsg5514
{
    private const double a = 6377397.15508;
    private const double e = 0.081696831215303;
    private const double n = 0.97992470462083;
    private const double konst_u_ro = 12310230.12797036;
    private const double sinUQ = 0.863499969506341;
    private const double cosUQ = 0.504348889819882;
    private const double sinVQ = 0.420215144586493;
    private const double cosVQ = 0.907424504992097;
    private const double alfa = 1.000597498371542;
    private const double k = 1.003419163966575;
    private const double f_1 = 299.152812853;
    private const double aWgs = 6378137.0;
    private const double f_1Wgs = 298.257223563;

    private const double dx = 570.69;
    private const double dy = 85.69;
    private const double dz = 462.84;
    private const double wz = -5.2611 / 3600 * Math.PI / 180;
    private const double wy = -1.58676 / 3600 * Math.PI / 180;
    private const double wx = -4.99821 / 3600 * Math.PI / 180;
    private const double m = 3.543e-6;

    // from https://web.archive.org/web/20070919182001/http://www.gpsweb.cz/JTSK-WGS.htm
    public static (float, float, float) ConvertToWgs84(float y0, float x0, float h0)
    {
        if (x0 > 0 || y0 > 0) throw new ArgumentException("Negative coordinates expected");
        double x = -x0;
        double y = -y0;
        var h = h0 + 45;

        // Vypocet zemepisnych souradnic z rovinnych souradnic
        var ro = Math.Sqrt(x * x + y * y);
        var epsilon = 2 * Math.Atan(y / (ro + x));
        var D = epsilon / n;
        var S = 2 * Math.Atan(Math.Exp(1 / n * Math.Log(konst_u_ro / ro))) - Math.PI / 2;
        var sinCosS = Math.SinCos(S);
        var sinS = sinCosS.Sin;
        var cosS = sinCosS.Cos;
        var sinCosD = Math.SinCos(D);
        var sinU = sinUQ * sinS - cosUQ * cosS * sinCosD.Cos;
        var cosU = Math.Sqrt(1 - sinU * sinU);
        var sinDV = sinCosD.Sin * cosS / cosU;
        var cosDV = Math.Sqrt(1 - sinDV * sinDV);
        var sinV = sinVQ * cosDV - cosVQ * sinDV;
        var cosV = cosVQ * cosDV + sinVQ * sinDV;
        var Ljtsk = 2 * Math.Atan(sinV / (1 + cosV)) / alfa;
        var t = Math.Exp(2 / alfa * Math.Log((1 + sinU) / cosU / k));
        var pom = (t - 1) / (t + 1);
        double sinB;
        do
        {
            sinB = pom;
            pom = t * Math.Exp(e * Math.Log((1 + e * sinB) / (1 - e * sinB)));
            pom = (pom - 1) / (pom + 1);
        } while (Math.Abs(pom - sinB) > 1e-15);
        var Bjtsk = Math.Atan(pom / Math.Sqrt(1 - pom * pom));

        // Pravoúhlé souřadnice ve S-JTSK
        var e2 = 1 - (1 - 1 / f_1) * (1 - 1 / f_1);
        var sinCosBjtsk = Math.SinCos(Bjtsk);
        var sinBjtsk = sinCosBjtsk.Sin;
        var cosBjtsk = sinCosBjtsk.Cos;
        var sinCosLjtsk = Math.SinCos(Ljtsk);
        var sinLjtsk = sinCosLjtsk.Sin;
        var cosLjtsk = sinCosLjtsk.Cos;
        ro = a / Math.Sqrt(1 - e2 * sinBjtsk * sinBjtsk);
        x = (ro + h) * cosBjtsk * cosLjtsk;
        y = (ro + h) * cosBjtsk * sinLjtsk;
        var z = ((1 - e2) * ro + h) * sinBjtsk;
        // Pravoúhlé souřadnice v WGS-84
        var xn = dx + (1 + m) * (x + wz * y - wy * z);
        var yn = dy + (1 + m) * (-wz * x + y + wx * z);
        var zn = dz + (1 + m) * (wy * x - wx * y + z);
        // Geodetické souřadnice v systému WGS-84
        var a_b = f_1Wgs / (f_1Wgs - 1);
        var p = Math.Sqrt(xn * xn + yn * yn);
        e2 = 1 - (1 - 1 / f_1Wgs) * (1 - 1 / f_1Wgs);
        var theta = Math.Atan(zn * a_b / p);
        var sinCosT = Math.SinCos(theta);
        var st = sinCosT.Sin;
        var ct = sinCosT.Cos;
        t = (zn + e2 * a_b * aWgs * st * st * st) / (p - e2 * aWgs * ct * ct * ct);
        var B = Math.Atan(t);
        var L = 2 * Math.Atan(yn / (p + xn));
        var H = Math.Sqrt(1 + t * t) * (p - aWgs / Math.Sqrt(1 + (1 - e2) * t * t));

        // Formát výstupních hodnot

        B = B / Math.PI * 180;
        L = L / Math.PI * 180;
        H = H - 45;

        return ((float)Math.Round(B, 3), (float)Math.Round(L, 3), (float)Math.Round(H));
    }
}