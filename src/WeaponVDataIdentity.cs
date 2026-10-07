using System;

namespace RecoilProbe {
 internal static class WeaponVDataIdentity {
  internal static bool Matches(int definition,string entityName,string vdataName) {
   if(String.IsNullOrEmpty(entityName)||String.IsNullOrEmpty(vdataName))return false;
   // M4A1-S uses item definition 60 but can expose the base M4 entity classname.
   // The variant VData must still be the silenced weapon; never accept M4A4 parameters for ID 60.
   if(definition==60)return vdataName=="weapon_m4a1_silencer"&&
    (entityName=="weapon_m4a1"||entityName=="weapon_m4a1_silencer");
   if(entityName=="weapon_m4a1_silencer"||vdataName=="weapon_m4a1_silencer")return false;
   return String.Equals(entityName,vdataName,StringComparison.Ordinal);
  }
 }
}
