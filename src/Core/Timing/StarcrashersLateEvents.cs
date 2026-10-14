using System;
using System.Collections.Generic;
using System.Linq;

namespace KuroakiGimmick.Core;

/// <summary>
/// Deterministic, seekable implementation of Starcrashers' late diamond-dust callbacks.
/// Source: obj_starcrashers_gimmick.Create, Particle_Spawners, o_pt_diamonddust_invmask.Step.
/// The original RNG and global.op_bgparticles setting are not in the GML dump;
/// the fixed seed and default particle density are explicit replay assumptions.
/// </summary>
public sealed class StarcrashersLateEvents
{
    public readonly record struct Dust(int Birth, int Death, float X, float Y, float Vx, int Frame);
    public List<Dust> Diamonds { get; } = [];
    const int MaxDust = 25000;
    private uint seed = 0xC76E9A21;
    private readonly Timeline timeline;

    // Matches the 60Hz movement domain. Deliberately does not use render frame delta.
    private double Next()
    {
        seed ^= seed << 13;
        seed ^= seed >> 17;
        seed ^= seed << 5;
        return seed / 4294967296.0;
    }
    private int Integer(int low, int high) => low + Math.Min(high - low, (int)(Next() * (high - low + 1)));

    public StarcrashersLateEvents(Session session) : this(session.Timeline, session.Chart, session.Duration) { }

    public StarcrashersLateEvents(Timeline timeline, Chart chart, double duration)
    {
        this.timeline = timeline;
        var events = chart.Mods
            .Where(e => e.Name == "chorus_satalli_alli_particles" || e.Name == "chorus_satalli_sat_particles")
            .OrderBy(e => e.Beat).ThenBy(e => e.Order);
        int finalTick = Math.Clamp((int)Math.Ceiling(duration * 60), 0, 60 * 3600);
        foreach (var evt in events)
        {
            int birth = Math.Max(0, (int)Math.Ceiling(timeline.Bpm.Time(evt.Beat) * 60 - 1e-8));
            bool fromLeft = evt.Name == "chorus_satalli_alli_particles";
            // addExtraMod registers a callback with arguments [start, duration, v1, v2, ease].
            // GML function(arg0,arg1,arg2,arg3) repeat(arg3) therefore reads the V2/To endpoint.
            int repeats = double.IsFinite(evt.To) ? (int)Math.Clamp(Math.Round(evt.To, MidpointRounding.ToEven), 0, 2500) : 0;
            for (int n = 0; n < repeats && Diamonds.Count < MaxDust; n++)
            {
                float y = Integer(5, 75); // one irandom_range() per spawn_particles_directional call
                // spawn_particles_directional(..., arg4=1, ...): for(i=0; i<=1/op_bgparticles; i++)
                // Standard op_bgparticles=1 emits TWO instances for each call, not one.
                for (int duplicate = 0; duplicate < 2 && Diamonds.Count < MaxDust; duplicate++)
                {
                    float speed = (float)(Next() * 0.5 * 8) * (fromLeft ? 1 : -1);
                    int frame = Integer(0, 4) % 4; // source irandom(image_number), inclusive upper bound
                    float x = fromLeft ? -10 : 330;
                    var dust = new Dust(birth, finalTick + 1, x, y, speed, frame);
                    for (int tick = birth; tick <= finalTick; tick++)
                        if (PositionAt(dust, tick).X is >= 120 and <= 200)
                        { dust = dust with { Death = tick }; break; }
                    Diamonds.Add(dust);
                }
            }
        }
        Diamonds.Sort(static (a, b) => a.Birth.CompareTo(b.Birth));
    }

    public (float X, float Y) PositionAt(Dust dust, int tick)
    {
        var offset = timeline.ParticleOffset(tick / 60.0) - timeline.ParticleOffset(Math.Max(0, dust.Birth - 1) / 60.0);
        return (dust.X + dust.Vx * (tick - dust.Birth + 1) + offset.X, dust.Y + offset.Y);
    }

    public static int FirstSince(List<Dust> dust, int tick)
    {
        int lo = 0, hi = dust.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (dust[mid].Birth < tick) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
