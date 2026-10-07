namespace RecoilProbe {
 internal static class WeaponVariant {
  internal static string DisplayName(GameIdentity identity) {
   if(identity.ItemDefinitionIndex==60&&identity.SilencerOn.HasValue)
    return identity.SilencerOn.Value?"M4A1-S con silenziatore":"M4A1-S senza silenziatore";
   return identity.WeaponName;
  }
  internal static string DisplayName(WeaponSnapshot data) {
   if(data.Native!=null&&data.Native.ItemDefinitionIndex==60) {
    if(data.Native.Mode==0)return "M4A1-S senza silenziatore";
    if(data.Native.Mode==1)return "M4A1-S con silenziatore";
   }
   return data.Weapon;
  }
  internal static string ExportName(WeaponSnapshot data) {
   if(data.Native!=null&&data.Native.ItemDefinitionIndex==60) {
    if(data.Native.Mode==0)return "M4A1_S_SENZA_SILENZIATORE";
    if(data.Native.Mode==1)return "M4A1_S_CON_SILENZIATORE";
   }
   return data.Weapon;
  }
 }
}
