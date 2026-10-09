using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace RecoilProbe {
 // Resolve the actual client cs_gamerules entity; do not guess a disputed global RVA.
 internal sealed class SessionRulesReference {
  internal long Proxy, Identity, Rules;
  private long namePointer;
  private uint reference;
  private static readonly byte[] Designer = Encoding.ASCII.GetBytes("cs_gamerules\0");
  private static uint Reference(byte[] node) {
   return unchecked(BitConverter.ToUInt32(node,Layout.EntityReferenceHandle)-
    ((BitConverter.ToUInt32(node,Layout.EntityFlags)&1U)<<15));
  }
  private static bool HasDesigner(ReadMemory memory,long name) {
   if(name==0)return false;
   for(int i=0;i<Designer.Length;i++)
    if(memory.Byte(name+i)!=Designer[i])return false;
   return true;
  }
  private static byte[] Node(ReadMemory memory,long at) {
   return memory.Bytes(at,Layout.EntityNext+8);
  }
  private static SessionRulesReference Match(ReadMemory memory,long identity,byte[] node) {
   long name=BitConverter.ToInt64(node,Layout.DesignerName);
   if(!HasDesigner(memory,name))return null;
   long proxy=BitConverter.ToInt64(node,0);
   if(memory.NamedPointer(proxy+Layout.EntityIdentity,"Identita' delle regole")!=identity)
    throw new InvalidOperationException("Identita' cs_gamerules non coerente. Ripeti la prova.");
   SessionRulesReference result=new SessionRulesReference {Proxy=proxy,Identity=identity,
    namePointer=name,reference=Reference(node),
    Rules=memory.NamedPointer(proxy+Layout.GameRulesProxy,"Regole della sessione (cs_gamerules)")};
   result.Validate(memory);
   return result;
  }
  internal static SessionRulesReference Read(ReadMemory memory,long pawn) {
   long root=memory.NamedPointer(pawn+Layout.EntityIdentity,"Identita' del giocatore locale");
   byte[] first=Node(memory,root);
   if(BitConverter.ToInt64(first,0)!=pawn)
    throw new InvalidOperationException("Identita' del giocatore locale non coerente.");
   SessionRulesReference found=Match(memory,root,first);
   HashSet<long> visited=new HashSet<long>();visited.Add(root);
   Stopwatch duration=Stopwatch.StartNew();
   foreach(bool forward in new bool[] {true,false}) {
    int next=forward?Layout.EntityNext:Layout.EntityPrevious;
    int opposite=forward?Layout.EntityPrevious:Layout.EntityNext;
    long previous=root,current=BitConverter.ToInt64(first,next);
    while(current!=0&&!visited.Contains(current)) {
     if(visited.Count>=IdentityReader.MaximumEntityLookup||duration.ElapsedMilliseconds>2000)
      throw new InvalidOperationException("Ricerca cs_gamerules interrotta: elenco entita' non stabile.");
     visited.Add(current);byte[] node=Node(memory,current);
     if(BitConverter.ToInt64(node,opposite)!=previous)
      throw new InvalidOperationException("Elenco entita' cambiato durante la ricerca cs_gamerules. Riprova.");
     SessionRulesReference candidate=Match(memory,current,node);
     if(candidate!=null) {
      if(found!=null)throw new InvalidOperationException("Piu' entita' cs_gamerules: regole della sessione ambigue.");
      found=candidate;
     }
     previous=current;current=BitConverter.ToInt64(node,next);
    }
    if(current!=0&&current!=root)
     throw new InvalidOperationException("Ciclo non valido nell'elenco cs_gamerules. Ripeti la prova.");
    if(current==root)break; // A complete circular list has already covered both directions.
   }
   if(found==null)
    throw new InvalidOperationException("Entita' cs_gamerules non trovata. Apri una mappa di pratica locale e riprova.");
   if(memory.NamedPointer(pawn+Layout.EntityIdentity,"Identita' del giocatore locale")!=root||
    BitConverter.ToInt64(Node(memory,root),0)!=pawn)
    throw new InvalidOperationException("Mappa cambiata durante la ricerca cs_gamerules. Riprova.");
   found.Validate(memory);
   return found;
  }
  internal void Validate(ReadMemory memory) {
   byte[] node=Node(memory,Identity);
   if(BitConverter.ToInt64(node,0)!=Proxy||Reference(node)!=reference||
    BitConverter.ToInt64(node,Layout.DesignerName)!=namePointer||!HasDesigner(memory,namePointer)||
    memory.NamedPointer(Proxy+Layout.EntityIdentity,"Identita' delle regole")!=Identity||
    memory.NamedPointer(Proxy+Layout.GameRulesProxy,"Regole della sessione (cs_gamerules)")!=Rules)
    throw new InvalidOperationException("Regole della sessione cambiate o identita' non valida. Ripeti la prova.");
  }
 }
}
