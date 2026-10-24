using System;
using System.Drawing;

namespace ZowieBatteryTray
{
    enum ReadStatus { Ok, NoReply, BadFormat, NoData }

    enum ChargeSource { None, Cable, Voltage }

    sealed class BatteryReading
    {
        public ReadStatus Status;
        public string Raw;
        public int Millivolts;
        public int Percent;
        public ChargeSource Charging;
    }

    static class Battery
    {
        public const int LowMillivolts = 3680;       // el LED del ratón avisa de batería baja (<= 10 %)
        public const int QuarterMillivolts = 3760;   // <= 25 %
        public const int HalfMillivolts = 3890;      // <= 50 %
        // En reposo una batería de litio no pasa de 4,2 V: por encima tiene que estar cargando
        // (o se acaba de sacar del cargador).
        public const int ChargingMillivolts = 4250;

        public static BatteryReading Read(ZowieDevice device)
        {
            var reading = new BatteryReading();
            byte[] reply = ZowieHid.ReadBatteryReport(device, 1500);
            if (reply == null) { reading.Status = ReadStatus.NoReply; return reading; }
            reading.Raw = BitConverter.ToString(reply).Replace('-', ' ');
            if (reply[1] == 0xE6 || reply[4] != 0xC8) { reading.Status = ReadStatus.BadFormat; return reading; }
            reading.Millivolts = (reply[1] * 256 + reply[2]) * 10;
            if (reading.Millivolts == 0) { reading.Status = ReadStatus.NoData; return reading; }
            reading.Status = ReadStatus.Ok;
            reading.Percent = EstimatePercent(reading.Millivolts);
            reading.Charging = device.IsCable ? ChargeSource.Cable
                : reading.Millivolts >= ChargingMillivolts ? ChargeSource.Voltage : ChargeSource.None;
            return reading;
        }

        // Estimación: los tres puntos del medio son los umbrales del LED del propio ratón (sacados de su
        // firmware); el resto, valores típicos de una batería de litio. Mientras carga sale más alto.
        static readonly int[,] Curve = { { 3500, 0 }, { 3680, 10 }, { 3760, 25 }, { 3890, 50 }, { 4000, 75 }, { 4200, 100 } };

        public static int EstimatePercent(int millivolts)
        {
            if (millivolts <= Curve[0, 0]) return 0;
            for (int i = 1; i < Curve.GetLength(0); i++)
            {
                if (millivolts <= Curve[i, 0])
                {
                    double t = (double)(millivolts - Curve[i - 1, 0]) / (Curve[i, 0] - Curve[i - 1, 0]);
                    return (int)Math.Round(Curve[i - 1, 1] + (Curve[i, 1] - Curve[i - 1, 1]) * t);
                }
            }
            return 100;
        }

        // Colores de la insignia con los mismos tramos que el LED del ratón.
        public static void BandColors(int millivolts, out Color back, out Color fore)
        {
            if (millivolts <= LowMillivolts) { back = Color.FromArgb(0xFF, 0x5F, 0x57); fore = Color.White; }
            else if (millivolts <= QuarterMillivolts) { back = Color.FromArgb(0xFF, 0x9F, 0x0A); fore = Color.White; }
            else if (millivolts <= HalfMillivolts) { back = Color.FromArgb(0xFF, 0xD6, 0x0A); fore = Color.FromArgb(0x1A, 0x1A, 0x1A); }
            else { back = Color.FromArgb(0x32, 0xD7, 0x4B); fore = Color.FromArgb(0x06, 0x28, 0x0D); }
        }
    }

    // Por radio solo llega la tensión, así que la carga en la base se deduce de cómo evoluciona:
    // - cargando, la tensión se mantiene o sube (en la base se queda fija, p. ej. en 4,39 V);
    // - al quitarlo de la base baja, primero de golpe y luego despacio: 0,02 V por debajo del máximo
    //   ya cuenta como "no carga", aunque siga por encima de 4,25 V un buen rato;
    // - al volver a ponerlo sube de golpe: 0,05 V por encima del mínimo vuelve a ser "cargando".
    // En la primera lectura no hay con qué comparar y se usa el umbral de 4,25 V.
    sealed class ChargeTrend
    {
        bool hasReading;
        bool charging;
        int peak, trough;

        public void Reset() { hasReading = false; charging = false; }

        public bool Update(int millivolts)
        {
            if (!hasReading)
            {
                hasReading = true;
                charging = millivolts >= Battery.ChargingMillivolts;
                peak = trough = millivolts;
            }
            else if (charging)
            {
                peak = Math.Max(peak, millivolts);
                if (millivolts <= peak - 20) { charging = false; trough = millivolts; }
            }
            else
            {
                trough = Math.Min(trough, millivolts);
                if (millivolts >= trough + 50) { charging = true; peak = millivolts; }
            }
            return charging;
        }
    }
}
