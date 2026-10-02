using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RecoilProbe {
 internal sealed class GameIdentity {
  public string WeaponName, DesignerName;
  public int ItemDefinitionIndex, Ammo;
  public uint WeaponHandle;
  public float Sensitivity, PawnMouseSensitivity, FovSensitivityAdjust;
  public bool Scoped;
  internal long WeaponAddress;
 }
 internal static class WeaponCatalog {
  private static readonly Dictionary<int,string> Names = new Dictionary<int,string> {
   {1,"DEAGLE"},{2,"DUAL_BERETTAS"},{3,"FIVE_SEVEN"},{4,"GLOCK"},
   {7,"AK47"},{8,"AUG"},{9,"AWP"},{10,"FAMAS"},{11,"G3SG1"},{13,"GALIL_AR"},
   {14,"M249"},{16,"M4A4"},{17,"MAC10"},{19,"P90"},{23,"MP5_SD"},{24,"UMP45"},
   {25,"XM1014"},{26,"PP_BIZON"},{27,"MAG7"},{28,"NEGEV"},{29,"SAWED_OFF"},
   {30,"TEC9"},{31,"ZEUS"},{32,"P2000"},{33,"MP7"},{34,"MP9"},{35,"NOVA"},
   {36,"P250"},{38,"SCAR20"},{39,"SG553"},{40,"SSG08"},{60,"M4A1_S"},{61,"USP_S"},
   {63,"CZ75_AUTO"},{64,"R8_REVOLVER"}
  };
  internal static string Name(int definition) {
   string name;
   if (!Names.TryGetValue(definition, out name))
    throw new InvalidOperationException("Equipaggia un'arma da fuoco riconosciuta (ID " + definition + ").");
   return name;
  }
  internal static bool IsFullAuto(int definition) {
   return Array.IndexOf(new int[] {7,8,10,13,14,16,17,19,23,24,26,28,33,34,39,60,63}, definition) >= 0;
  }
 }
 internal static class IdentityReader {
  internal const int EntityStride = 0x70;
  internal const int EntryMask = 0x7FFF;
  internal static bool ValidSensitivity(double value) {
   return !Double.IsNaN(value) && !Double.IsInfinity(value) && value >= 0.0001 && value <= 100;
  }
  internal static string ReadName(ReadMemory memory, long address) {
   StringBuilder result = new StringBuilder();
   for (int i = 0; i < 80; i++) {
    byte value = memory.Byte(address + i);
    if (value == 0) return result.ToString();
    if (value < 32 || value > 126) throw new InvalidOperationException("Nome arma in memoria non valido.");
    result.Append((char)value);
   }
   throw new InvalidOperationException("Nome arma troppo lungo.");
  }
  internal static long Resolve(ReadMemory memory, long list, uint handle) {
   if (handle == 0xFFFFFFFF || (handle & EntryMask) == 0)
    throw new InvalidOperationException("Nessuna arma attiva.");
   int index = (int)(handle & EntryMask);
   long chunk = memory.Pointer(list + 0x10 + 8L * (index >> 9));
   long identity = chunk + EntityStride * (index & 0x1FF);
   long entity = memory.Pointer(identity);
   if (memory.Pointer(entity + Layout.EntityIdentity) != identity)
    throw new InvalidOperationException("Identita' dell'arma non coerente con la lista entita'.");
   uint stored = BitConverter.ToUInt32(memory.Bytes(identity + 0x10, 4), 0);
   uint flags = BitConverter.ToUInt32(memory.Bytes(identity + 0x30, 4), 0);
   uint reference = unchecked(stored - ((flags & 1U) << 15));
   if (reference != handle)
    throw new InvalidOperationException("Handle dell'arma scaduto. Riprova quando l'arma e' equipaggiata.");
   return entity;
  }
  internal static GameIdentity Read(ReadMemory memory, long client, long pawn, GameIdentity cached) {
   long services = memory.Pointer(pawn + Layout.WeaponServices);
   uint before = BitConverter.ToUInt32(memory.Bytes(services + Layout.ActiveWeapon, 4), 0);
   long sensPointer = memory.Pointer(client + Layout.Sensitivity);
   GameIdentity result = new GameIdentity();
   result.WeaponHandle = before;
   if (cached != null && cached.WeaponHandle == before) {
    result.WeaponAddress = cached.WeaponAddress;
    result.ItemDefinitionIndex = cached.ItemDefinitionIndex;
    result.WeaponName = cached.WeaponName; result.DesignerName = cached.DesignerName;
   } else {
    result.WeaponAddress = Resolve(memory, memory.Pointer(client + Layout.EntityList), before);
    result.ItemDefinitionIndex = BitConverter.ToUInt16(memory.Bytes(result.WeaponAddress +
     Layout.AttributeManager + Layout.ItemView + Layout.ItemDefinitionIndex, 2), 0);
    result.WeaponName = WeaponCatalog.Name(result.ItemDefinitionIndex);
    long identity = memory.Pointer(result.WeaponAddress + Layout.EntityIdentity);
    result.DesignerName = ReadName(memory, memory.Pointer(identity + Layout.DesignerName));
    if (!result.DesignerName.StartsWith("weapon_", StringComparison.Ordinal))
     throw new InvalidOperationException("L'entita' equipaggiata non e' un'arma.");
   }
   result.Sensitivity = memory.Float(sensPointer + Layout.SensitivityValue);
   result.PawnMouseSensitivity = memory.Float(pawn + Layout.PawnMouseSensitivity);
   result.FovSensitivityAdjust = memory.Float(pawn + Layout.FovSensitivityAdjust);
   result.Scoped = memory.Byte(pawn + Layout.Scoped) != 0;
   result.Ammo = memory.Int(result.WeaponAddress + Layout.WeaponClip);
   if (!ValidSensitivity(result.Sensitivity) || result.Ammo < 0 || result.Ammo > 1000)
    throw new InvalidOperationException("Sensibilita' o munizioni non plausibili. Lettura interrotta.");
   if (FloatInvalid(result.PawnMouseSensitivity) || FloatInvalid(result.FovSensitivityAdjust))
    throw new InvalidOperationException("Scala mouse/FOV non valida.");
   uint after = BitConverter.ToUInt32(memory.Bytes(services + Layout.ActiveWeapon, 4), 0);
   if (before != after) throw new InvalidOperationException("Arma cambiata durante la lettura.");
   return result;
  }
  private static bool FloatInvalid(float f) { return Single.IsNaN(f) || Single.IsInfinity(f); }
 }
}
