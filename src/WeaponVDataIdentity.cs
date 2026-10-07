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
   // MP5-SD reuses the MP7 entity classname but must read its own variant VData.
   if(definition==23)return vdataName=="weapon_mp5sd"&&
    (entityName=="weapon_mp7"||entityName=="weapon_mp5sd");
   if(entityName=="weapon_mp5sd"||vdataName=="weapon_mp5sd")return false;
   return String.Equals(entityName,vdataName,StringComparison.Ordinal);
  }
 }
}
