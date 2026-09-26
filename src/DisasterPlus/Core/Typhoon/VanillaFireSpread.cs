namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// A model of **vanilla's fire-spread probability division**, so that ④ does not fall
    /// through it.
    /// <b>This is Core, so it never touches the engine</b> — all that is here is the
    /// <b>arithmetic fact</b> of when that division divides by zero.
    ///
    /// ── This crashed on real hardware (DivideByZeroException) ─────────────────
    ///
    /// The IL of <c>ThunderStormAI.GetFireSpreadProbability</c> (measured in this task):
    /// <code>
    /// IL_003E  num = (int)(SimulationManager.m_currentFrameIndex - data.m_activationFrame)
    /// IL_0050  ldc.i4 1500
    /// IL_0055  ldc.i4.8
    /// IL_0056  ldloc.0
    /// IL_0057  ldc.i4.s 10
    /// IL_0059  shr            // ★ not shr.un, i.e. num is signed
    /// IL_005A  add
    /// IL_005B  div            // ★ integer division. Dividing by 0 throws.
    /// </code>
    /// That is, <c>1500 / (8 + (num &gt;&gt; 10))</c>.
    /// When <c>num</c> is negative the arithmetic shift **rounds downwards**, so for
    /// <c>num ∈ [<see cref="UnsafeElapsedFirst"/>, <see cref="UnsafeElapsedLast"/>]</c> it
    /// comes out at exactly <c>8 + (-8) = 0</c>, and **for those 1,024 frames alone it
    /// divides by zero.** Below that, <c>num</c> merely makes the quotient negative and
    /// throws nothing — **only inside the window is it dangerous**, which is why it can
    /// "happen to work".
    ///
    /// ── Why vanilla does not crash but ④ did ──────────────────────
    ///
    /// <c>num</c> goes negative when <c>m_activationFrame</c> lies in the future — that is,
    /// while the disaster is **Emerging**. <c>StartDisaster</c> writes
    /// <c>m_activationFrame = m_startFrame + m_emergingDuration</c> (measured from the IL),
    /// and the Thunderstorm prefab's <c>m_emergingDuration</c> is <b>8192</b> (measured from
    /// sharedassets55, in the same run of fields as <c>m_radius</c> 4000 /
    /// <c>m_activeDuration</c> 8192). So the moment a typhoon is triggered, <c>num</c> is
    /// exactly <c>-8192</c> = **the left edge of the dangerous window**.
    ///
    /// This division is only called when <b>a building or tree belonging to the disaster
    /// group is burning</b> (<c>CommonBuildingAI.HandleFireSpread</c> /
    /// <c>TreeManager.HandleFireSpread</c>). Vanilla's storm does not start fires until it
    /// goes Active, so it is only ever called with <c>num &gt;= 0</c>. ④, however,
    /// **scatters lightning from the moment you click** (<c>TyphoonLightning</c>), so fires
    /// start while it is still Emerging, and that is the first time this division runs with
    /// a negative <c>num</c>.
    ///
    /// ── ④'s remedy (<c>TyphoonSlot.Begin</c>) ───────────────────────────
    ///
    /// **Pull <c>m_activationFrame</c> back to the start frame.** ④'s typhoon is a thing
    /// that "starts right there the moment you press it"; it has no 8,192-frame emerging
    /// period (give it one and a typhoon with an 8,192-frame duration would <b>spend its
    /// entire life Emerging and then end</b>). With that, <c>num</c> is always 0 or above
    /// and the divisor is fixed at <see cref="MinSafeDivisor"/> or more — **the route into
    /// the window disappears entirely.**
    /// </summary>
    public static class VanillaFireSpread
    {
        /// <summary>The dividend (<c>ldc.i4 1500</c>). ④ does not use the value itself, but
        /// we keep it on record as a fact.</summary>
        public const int Numerator = 1500;

        /// <summary>The constant term of the divisor (<c>ldc.i4.8</c>).</summary>
        public const int DivisorBase = 8;

        /// <summary>The arithmetic shift applied to the elapsed frames
        /// (<c>ldc.i4.s 10</c>).</summary>
        public const int ElapsedShift = 10;

        /// <summary>The smallest divisor when <c>num &gt;= 0</c>. The same as
        /// <see cref="DivisorBase"/>.</summary>
        public const int MinSafeDivisor = DivisorBase;

        /// <summary>The lower end (inclusive) of the <c>num</c> range that divides by zero.
        /// <c>-8 * 1024</c>.</summary>
        public const int UnsafeElapsedFirst = -(DivisorBase << ElapsedShift);

        /// <summary>The upper end (inclusive) of the <c>num</c> range that divides by zero.
        /// <c>-7 * 1024 - 1</c>.</summary>
        public const int UnsafeElapsedLast = -((DivisorBase - 1) << ElapsedShift) - 1;

        /// <summary>
        /// The elapsed frame count <c>num</c> as vanilla computes it. <b>Returned narrowed
        /// to a signed value</b> — because vanilla itself reads it with <c>shr</c> (an
        /// arithmetic shift), and if we kept it as a <c>uint</c> here then "activation in the
        /// future" would turn into an enormous positive number, erasing the very fact this
        /// type exists to guard.
        /// </summary>
        public static int ElapsedOf(uint currentFrame, uint activationFrame)
        {
            return unchecked((int)(currentFrame - activationFrame));
        }

        /// <summary>
        /// The divisor vanilla actually uses, <c>8 + (num &gt;&gt; 10)</c>.
        /// **It can return 0** (which is this type's reason for existing).
        /// </summary>
        public static int DivisorOf(int elapsed)
        {
            return DivisorBase + (elapsed >> ElapsedShift);
        }

        /// <summary>Whether vanilla's division divides by zero at this <c>num</c>.</summary>
        public static bool DividesByZero(int elapsed)
        {
            return DivisorOf(elapsed) == 0;
        }

        /// <summary>
        /// Whether this activation frame means it will never divide by zero, <b>from this
        /// frame onwards for good</b>.
        ///
        /// If <c>activationFrame &lt;= currentFrame</c> then <c>num</c> starts at 0 or above
        /// and only increases monotonically, so the divisor stays at
        /// <see cref="MinSafeDivisor"/> or more. An activation in the future, by contrast,
        /// cannot be called safe even if we are currently outside the window, because it
        /// <b>will pass through the window eventually</b> (<c>num</c> grows by 1 every
        /// frame).
        /// </summary>
        public static bool IsSafeActivation(uint currentFrame, uint activationFrame)
        {
            return activationFrame <= currentFrame;
        }

        /// <summary>
        /// The value ④ should write to <c>m_activationFrame</c>. It is <b>"activate right
        /// now"</b>.
        ///
        /// <paramref name="startFrame"/> is the <c>m_startFrame</c> written by
        /// <c>StartDisaster</c> (i.e. the frame the typhoon was triggered on). As a rule we
        /// return it unchanged, but **0 alone is raised to 1** — <c>m_activationFrame == 0</c>
        /// carries a different meaning, "no activation is scheduled"
        /// (the <c>brfalse</c> at <c>IL_0015</c> in <c>ThunderStormAI.IsStillEmerging</c>
        /// treats 0 as permanently Emerging), and writing it through would leave the disaster
        /// stuck Emerging forever, eating one slot.
        ///
        /// For the single frame where we return 1, <c>num</c> is <c>-1</c>, but the divisor
        /// is <c>8 + (-1 &gt;&gt; 10) = 7</c>, which is not 0.
        /// </summary>
        public static uint SafeActivationFrame(uint startFrame)
        {
            return startFrame == 0u ? 1u : startFrame;
        }
    }
}
