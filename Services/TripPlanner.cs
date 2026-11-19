using EvNav.Models;

namespace EvNav.Services;

public static class TripPlanner
{
    private const double ChargePowerFactor = 0.65;

    public static TripPlan Plan(RouteResult route, IReadOnlyList<ChargingOption> options, VehicleProfile p)
    {
        var plan = new TripPlan();
        double cons = Math.Max(5.0, p.ConsumptionKwhPer100Km) / 100.0;
        double cap = Math.Max(5.0, p.BatteryKwh);
        double pos = 0;
        double soc = Math.Clamp(p.StartSocPct, 5, 100);

        for (int guard = 0; guard < 60; guard++)
        {
            double reach = (soc - p.MinArrivalSocPct) / 100.0 * cap / cons;
            double destSoc = soc - (route.DistanceKm - pos) * cons / cap * 100.0;
            if (route.DistanceKm - pos <= reach + 1e-6 && destSoc >= p.MinDestinationSocPct)
            {
                plan.Success = true;
                plan.ArrivalSocPct = destSoc;
                break;
            }

            ChargingOption? best = null;
            double bestScore = double.NegativeInfinity;
            foreach (var o in options)
            {
                double progress = o.AlongKm - pos;
                if (progress <= 0.5) continue;
                if (o.DetourKm * 0.5 > p.MaxDetourKm) continue;
                double outKm = progress + o.DetourKm * 0.5;
                if (outKm > reach) continue;
                double arriveAtStation = soc - outKm * cons / cap * 100.0;
                if (arriveAtStation < p.MinArrivalSocPct) continue;
                if (arriveAtStation > p.MaxChargeSocPct - 2) continue;
                double power = Math.Min(o.Station.EffectivePowerKw, p.MaxDcPowerKw);
                double score = progress - o.DetourKm * 0.8 + Math.Min(power, 150.0) * 0.35;
                if (score > bestScore) { bestScore = score; best = o; }
            }

            if (best is null)
            {
                plan.Message = $"Keine nutzbare Ladestation im Reichweitenfenster bei km {pos:F0} " +
                               $"(Restreichweite ≈ {Math.Max(0, reach):F0} km).";
                plan.ArrivalSocPct = destSoc;
                break;
            }

            double outKmBest = (best.AlongKm - pos) + best.DetourKm * 0.5;
            double arrive = soc - outKmBest * cons / cap * 100.0;
            double remainingAfter = route.DistanceKm - best.AlongKm;
            double neededPct = (remainingAfter + best.DetourKm * 0.5) * cons / cap * 100.0
                               + p.MinDestinationSocPct + 5.0;
            double target = Math.Min(p.MaxChargeSocPct, Math.Max(arrive + 5.0, neededPct));

            double powerKw = Math.Min(best.Station.EffectivePowerKw, p.MaxDcPowerKw);
            double effKw = Math.Max(3.0, powerKw * ChargePowerFactor);
            double kwh = (target - arrive) / 100.0 * cap;
            double mins = kwh / effKw * 60.0;
            double resumeSoc = target - best.DetourKm * 0.5 * cons / cap * 100.0;

            plan.Stops.Add(new ChargingStop
            {
                Index = plan.Stops.Count + 1,
                Station = best.Station,
                AlongKm = best.AlongKm,
                DetourKm = best.DetourKm,
                ArriveSocPct = arrive,
                DepartSocPct = Math.Max(p.MinArrivalSocPct, resumeSoc),
                ChargeKwh = kwh,
                ChargeMin = mins
            });

            soc = resumeSoc;
            pos = best.AlongKm;
            plan.TotalChargeMin += mins;
            plan.TotalDetourKm += best.DetourKm;
            plan.ExtraDriveMin += best.DetourMin;
        }

        if (!plan.Success && plan.Message.Length == 0)
            plan.Message = "Planung abgebrochen (zu viele Ladestopps).";

        return plan;
    }
}
