using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Phoenix.FTL
{
    public enum JumpState
    {
        Idle = 0,
        Spooling,
        Jumped,
        Cooldown,
    }

    [Flags]
    public enum JumpFlags
    {
        None = 0,
        SlaveMode = 1,
        AbsolutePosition = 2,
        ExplicitCoords = 4,
        GPSWaypoint = 8,
        ShowCollision = 16,
        Disabled = 32,
    }

    public enum JumpSafety
    {
        None = 0,               // No jump safety, default
        Trail = 1,              // Spawn blocks every 10km or so between source and destination; doesn't work on DS
        Course,                 // Plot a jump course; no free floating people or objects
    }
}
