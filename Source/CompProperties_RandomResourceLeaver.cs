﻿using Verse;

namespace RemoteExplosives {
	public class CompProperties_RandomResourceLeaver : CompProperties {
		public ThingDef thingDef;
		public IntRange amountRange;
		public DestroyMode requiredDestroyMode = DestroyMode.Kill;

		public CompProperties_RandomResourceLeaver() {
			compClass = typeof (CompRandomResourceLeaver);
		}
	}
}