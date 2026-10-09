using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using RecoilProbe;

internal static class SessionRulesChecks {
 private static IntPtr Allocate(List<IntPtr> allocations,int size) {
  IntPtr at=Marshal.AllocHGlobal(size);allocations.Add(at);
  Marshal.Copy(new byte[size],0,at,size);return at;
 }
 private static IntPtr Name(List<IntPtr> allocations,string value) {
  IntPtr at=Marshal.StringToHGlobalAnsi(value);allocations.Add(at);return at;
 }
 private static void Node(IntPtr identity,IntPtr entity,IntPtr name,IntPtr previous,IntPtr next,uint handle,uint flags) {
  Marshal.WriteIntPtr(identity,0,entity);Marshal.WriteIntPtr(identity,Layout.DesignerName,name);
  Marshal.WriteInt32(identity,Layout.EntityReferenceHandle,unchecked((int)(handle+((flags&1U)<<15))));
  Marshal.WriteInt32(identity,Layout.EntityFlags,unchecked((int)flags));
  Marshal.WriteIntPtr(identity,Layout.EntityPrevious,previous);Marshal.WriteIntPtr(identity,Layout.EntityNext,next);
 }
 internal static void Run(Action<bool,string> check,Action<Action,string> reject) {
  List<IntPtr> allocations=new List<IntPtr>();
  try {
   IntPtr client=Allocate(allocations,0x2800000),engine=Allocate(allocations,0x920000);
   IntPtr pawn=Allocate(allocations,0x4000),network=Allocate(allocations,0x400);
   IntPtr rules=Allocate(allocations,0x200),otherRules=Allocate(allocations,0x200);
   IntPtr proxy=Allocate(allocations,0x700),decoyProxy=Allocate(allocations,0x700);
   IntPtr root=Allocate(allocations,0x70),decoy=Allocate(allocations,0x70),target=Allocate(allocations,0x70);
   IntPtr playerName=Name(allocations,"player"),realName=Name(allocations,"cs_gamerules");
   IntPtr wrongName=Name(allocations,"cs_gamerules_extra");
   const uint handle=(2U<<15)|37U;
   Node(root,pawn,playerName,IntPtr.Zero,decoy,66,0);
   Node(decoy,decoyProxy,wrongName,root,target,38,0);
   Node(target,proxy,realName,decoy,IntPtr.Zero,handle,1);
   Marshal.WriteIntPtr(pawn,Layout.EntityIdentity,root);
   Marshal.WriteIntPtr(proxy,Layout.EntityIdentity,target);Marshal.WriteIntPtr(proxy,Layout.GameRulesProxy,rules);
   Marshal.WriteIntPtr(decoyProxy,Layout.EntityIdentity,decoy);Marshal.WriteIntPtr(decoyProxy,Layout.GameRulesProxy,otherRules);
   Marshal.WriteIntPtr(client,Layout.LocalPawn,pawn);Marshal.WriteIntPtr(engine,Layout.NetworkClient,network);
   Marshal.WriteInt32(network,Layout.SignOnState,6);Marshal.WriteInt32(pawn,Layout.Health,100);
   // Conflicting published globals deliberately point elsewhere; neither can determine session rules.
   Marshal.WriteIntPtr(client,29023376,otherRules);Marshal.WriteIntPtr(client,39173760,otherRules);
   Marshal.WriteByte(otherRules,Layout.IsValveServer,1);
   using(Process own=Process.GetCurrentProcess())using(ReadMemory memory=new ReadMemory(own.Id)) {
    SessionRulesReference reference=SessionRulesReference.Read(memory,pawn.ToInt64());
    check(reference.Rules==rules.ToInt64()&&reference.Proxy==proxy.ToInt64()&&reference.Identity==target.ToInt64(),
     "14190 rules come from the exact cs_gamerules proxy and not either conflicting global");
    reference.Validate(memory);
    check(memory.PointerTrace.Contains("Regole della sessione (cs_gamerules)")&&
     memory.PointerTrace.Contains("Identita' delle regole"),"Rules diagnostics retain proxy identity and actual pointer source");
    Game session=(Game)FormatterServices.GetUninitializedObject(typeof(Game));
    session.Process=own;session.Memory=memory;session.Client=client.ToInt64();session.Engine=engine.ToInt64();
    session.Pawn=pawn.ToInt64();session.Rules=reference.Rules;session.RulesReference=reference;
    session.VerifySession();check(true,"The real session verification path accepts validated local rules");
    Marshal.WriteByte(rules,Layout.IsValveServer,1);
    reject(delegate{session.VerifySession();},"Valve sessions remain rejected after the rules resolver change");
    Marshal.WriteByte(rules,Layout.IsValveServer,2);
    reject(delegate{session.VerifySession();},"An invalid nonzero Valve flag cannot pass session validation");
    Marshal.WriteByte(rules,Layout.IsValveServer,0);Marshal.WriteInt32(network,Layout.SignOnState,5);
    reject(delegate{session.VerifySession();},"An unfinished map still fails sign-on verification");
    Marshal.WriteInt32(network,Layout.SignOnState,6);
    Marshal.WriteIntPtr(proxy,Layout.GameRulesProxy,otherRules);
    reject(delegate{session.VerifySession();},"Cached rules are invalidated when the proxy changes its rules pointer");
    Marshal.WriteIntPtr(proxy,Layout.GameRulesProxy,IntPtr.Zero);
    reject(delegate{SessionRulesReference.Read(memory,pawn.ToInt64());},"A named proxy with null rules cannot bypass validation");
    Marshal.WriteIntPtr(proxy,Layout.GameRulesProxy,rules);
    Marshal.WriteInt32(target,Layout.EntityReferenceHandle,unchecked((int)(handle+(2U<<15))));
    reject(delegate{reference.Validate(memory);},"Recycled rules entity references are rejected");
    Marshal.WriteInt32(target,Layout.EntityReferenceHandle,unchecked((int)(handle+(1U<<15))));
    Marshal.WriteIntPtr(proxy,Layout.EntityIdentity,decoy);
    reject(delegate{SessionRulesReference.Read(memory,pawn.ToInt64());},"A proxy must point back to its own entity identity");
    Marshal.WriteIntPtr(proxy,Layout.EntityIdentity,target);Marshal.WriteIntPtr(target,Layout.DesignerName,wrongName);
    reject(delegate{reference.Validate(memory);},"Cached rules reject a changed designer name");
    reject(delegate{SessionRulesReference.Read(memory,pawn.ToInt64());},"Designer prefixes do not substitute for exact cs_gamerules identity");
    Marshal.WriteIntPtr(target,Layout.DesignerName,realName);Marshal.WriteIntPtr(decoy,Layout.DesignerName,realName);
    reject(delegate{SessionRulesReference.Read(memory,pawn.ToInt64());},"Duplicate named rules proxies are rejected as ambiguous");
    Marshal.WriteIntPtr(decoy,Layout.DesignerName,wrongName);Marshal.WriteIntPtr(target,Layout.EntityPrevious,root);
    reject(delegate{SessionRulesReference.Read(memory,pawn.ToInt64());},"Corrupt reciprocal entity links cannot select session rules");
    Marshal.WriteIntPtr(target,Layout.EntityPrevious,decoy);Marshal.WriteIntPtr(target,Layout.EntityNext,decoy);
    Stopwatch duration=Stopwatch.StartNew();
    reject(delegate{SessionRulesReference.Read(memory,pawn.ToInt64());},"Rules lookup rejects a non-root cycle");
    check(duration.ElapsedMilliseconds<1000,"A cyclic rules lookup exits without waiting for the timeout");
    // A valid circular list is different from an inner cycle and is traversed exactly once.
    Marshal.WriteIntPtr(target,Layout.EntityNext,root);Marshal.WriteIntPtr(root,Layout.EntityPrevious,target);
    check(SessionRulesReference.Read(memory,pawn.ToInt64()).Rules==rules.ToInt64(),
     "A complete reciprocal circular list resolves a unique rules proxy");
    // Put the proxy before the local pawn to exercise the opposite linked-list direction.
    Node(target,proxy,realName,IntPtr.Zero,root,handle,1);
    Node(root,pawn,playerName,target,decoy,66,0);Node(decoy,decoyProxy,wrongName,root,IntPtr.Zero,38,0);
    check(SessionRulesReference.Read(memory,pawn.ToInt64()).Rules==rules.ToInt64(),
     "Rules before the local pawn are found through previous identity links");
    Marshal.WriteIntPtr(root,0,decoyProxy);
    reject(delegate{SessionRulesReference.Read(memory,pawn.ToInt64());},"A changed pawn identity aborts rules discovery");
    Marshal.WriteIntPtr(root,0,pawn);session.RulesReference=null;
    reject(delegate{session.VerifySession();},"No unvalidated session rules fallback is accepted");
   }
  } finally {foreach(IntPtr at in allocations)Marshal.FreeHGlobal(at);}
 }
}
