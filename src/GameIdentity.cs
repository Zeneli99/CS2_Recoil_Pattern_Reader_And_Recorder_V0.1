using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
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
  internal const int MaximumEntityLookup = 32768;
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
  private static uint Reference(byte[] identity) {
   uint stored=BitConverter.ToUInt32(identity,Layout.EntityReferenceHandle);
   uint flags=BitConverter.ToUInt32(identity,Layout.EntityFlags);
   return unchecked(stored-((flags&1U)<<15));
  }
  private static byte[] ReadNode(ReadMemory memory,long address) {
   try{return memory.Bytes(address,Layout.EntityNext+8);}
   catch(Exception ex){throw new MemoryFieldException("Catena delle identita' entita'",address,null,ex);}
  }
  private static long Match(ReadMemory memory,long address,byte[] node,uint handle) {
   if(Reference(node)!=handle)return 0;
   long entity=BitConverter.ToInt64(node,0);
   if(entity<0x10000||entity>0x00007FFFFFFFFFFF)
    throw new MemoryFieldException("Entita' dell'arma attiva",address,entity,null);
   if(memory.NamedPointer(entity+Layout.EntityIdentity,"Identita' dell'arma")!=address)
    throw new InvalidOperationException("Identita' dell'arma non coerente con il suo riferimento.");
   return entity;
  }
  internal static long Resolve(ReadMemory memory,long pawn,uint handle) {
   if(handle==0xFFFFFFFF||(handle&EntryMask)==0)
    throw new InvalidOperationException("Nessuna arma attiva.");
   long root=memory.NamedPointer(pawn+Layout.EntityIdentity,"Identita' del giocatore locale");
   byte[] first=ReadNode(memory,root);
   if(BitConverter.ToInt64(first,0)!=pawn)
    throw new InvalidOperationException("Identita' del giocatore locale non coerente.");
   long match=Match(memory,root,first,handle);if(match!=0)return match;
   HashSet<long> visited=new HashSet<long>();visited.Add(root);
   Stopwatch duration=Stopwatch.StartNew();
   foreach(bool forward in new bool[]{true,false}) {
    int nextField=forward?Layout.EntityNext:Layout.EntityPrevious;
    int oppositeField=forward?Layout.EntityPrevious:Layout.EntityNext;
    long previous=root,current=BitConverter.ToInt64(first,nextField);
    while(current!=0 && !visited.Contains(current)) {
     if(visited.Count>=MaximumEntityLookup||duration.ElapsedMilliseconds>2000)
      throw new InvalidOperationException("Riconoscimento arma interrotto: elenco entita' non stabile.");
     visited.Add(current);byte[] node=ReadNode(memory,current);
     if(BitConverter.ToInt64(node,oppositeField)!=previous)
      throw new InvalidOperationException("Elenco entita' cambiato durante il riconoscimento. Riprova.");
     match=Match(memory,current,node,handle);if(match!=0)return match;
     previous=current;current=BitConverter.ToInt64(node,nextField);
    }
   }
   throw new InvalidOperationException("Arma attiva non trovata nella catena delle entita'. Riprova con l'arma equipaggiata.");
  }
  private static void VerifyCachedReference(ReadMemory memory,GameIdentity cached,uint handle) {
   long identity=memory.NamedPointer(cached.WeaponAddress+Layout.EntityIdentity,"Identita' dell'arma");
   byte[] node=ReadNode(memory,identity);
   if(BitConverter.ToInt64(node,0)!=cached.WeaponAddress||Reference(node)!=handle)
    throw new InvalidOperationException("Riferimento dell'arma non piu' valido. Ripeti la registrazione.");
  }
  internal static GameIdentity Read(ReadMemory memory, long client, long pawn, GameIdentity cached) {
   long services = memory.NamedPointer(pawn + Layout.WeaponServices,"Servizi delle armi");
   uint before = BitConverter.ToUInt32(memory.Bytes(services + Layout.ActiveWeapon, 4), 0);
   long sensPointer = memory.NamedPointer(client + Layout.Sensitivity,"Impostazione sensibilita\'");
   GameIdentity result = new GameIdentity();
   result.WeaponHandle = before;
   if (cached != null && cached.WeaponHandle == before) {
    VerifyCachedReference(memory,cached,before);
    result.WeaponAddress = cached.WeaponAddress;
    result.ItemDefinitionIndex = cached.ItemDefinitionIndex;
    result.WeaponName = cached.WeaponName; result.DesignerName = cached.DesignerName;
   } else {
    result.WeaponAddress = Resolve(memory,pawn,before);
    result.ItemDefinitionIndex = BitConverter.ToUInt16(memory.Bytes(result.WeaponAddress +
     Layout.AttributeManager + Layout.ItemView + Layout.ItemDefinitionIndex, 2), 0);
    result.WeaponName = WeaponCatalog.Name(result.ItemDefinitionIndex);
    long identity = memory.NamedPointer(result.WeaponAddress + Layout.EntityIdentity,"Identita\' dell\'arma");
    result.DesignerName = ReadName(memory, memory.NamedPointer(identity + Layout.DesignerName,"Nome dell\'arma"));
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
