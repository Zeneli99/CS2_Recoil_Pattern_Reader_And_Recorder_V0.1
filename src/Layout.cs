// Field offsets: a2x/cs2-dumper, MIT, commit 4116de000e085d62bbd42334c67b35bda37bda4f.
// Snapshot 2026-10-06; target game build 14189. Fields checked against the pinned dump; live CS2 validation is still required.
namespace RecoilProbe {
 internal static class Layout {
  internal const int TargetBuild = 14189;
  internal const string SourceCommit = "4116de000e085d62bbd42334c67b35bda37bda4f";
  // The dump global dwEntityList reads 0x1 in the tested 14188 session; use pawn identity links.
  internal const int EntityPrevious = 0x50;
  internal const int EntityNext = 0x58;
  internal const int EntityFlags = 0x30;
  internal const int EntityReferenceHandle = 0x10;
  internal const int Sensitivity = 0x255F998;
  internal const int SensitivityValue = 0x58;
  internal const int WeaponServices = 0x12F0;
  internal const int ActiveWeapon = 0x60;
  internal const int EntityIdentity = 0x10;
  internal const int DesignerName = 0x20;
  internal const int AttributeManager = 0x1290;
  internal const int ItemView = 0x50;
  internal const int ItemDefinitionIndex = 0x1BA;
  internal const int Scoped = 0x1EA0;
  internal const int CameraServices = 0x1328;
  internal const int InputViewAngle = 0x13A8;
  internal const int CameraViewPunchAngle = 0x48;
  internal const int CameraViewPunchTick = 0x54;
  internal const int CameraViewPunchTickRatio = 0x58;
  internal const int PawnMouseSensitivity = 0x14A0;
  internal const int FovSensitivityAdjust = 0x149C;
  internal const int WeaponClip = 0x1928;
  internal const int WeaponRecoilIndex = 0x1A24;
  internal const int WeaponRecoilIndexFloat = 0x1A28;
  internal const int WeaponLastShotTime = 0x1B68;
  // Non-schema VData pointer candidate. Accepted only after name, bounds and stable-read checks.
  internal const int WeaponVData = 0x388;
  internal const int WeaponMode = 0x1A00;
  internal const int WeaponBurst = 0x1A2C;
  internal const int WeaponReloading = 0x1A3C;
  internal const int WeaponSilencerOn = 0x1A51;
  internal const int VDataMaxClip = 0x4D0;
  internal const int VDataName = 0x720;
  internal const int VDataFullAuto = 0x72D;
  internal const int VDataBullets = 0x730;
  internal const int VDataCycle = 0x738;
  internal const int VDataRecoilAngle = 0x790;
  internal const int VDataRecoilAngleVariance = 0x798;
  internal const int VDataRecoilMagnitude = 0x7A0;
  internal const int VDataRecoilMagnitudeVariance = 0x7A8;
  internal const int VDataRecoilSeed = 0x7D4;
  internal const int LocalPawn = 0x2562808;
  internal const int LocalController = 0x253A068;
  internal const int ViewAngles = 0x25787E8;
  internal const int GameRules = 0x255EE50;
  internal const int BuildNumber = 0x61CFE8;
  internal const int NetworkClient = 0x91AFC0;
  internal const int SignOnState = 0x230;
  internal const int ClientTick = 0x398;
  internal const int Health = 0x34C;
  internal const int LifeState = 0x354;
  internal const int ShotsFired = 0x1EB4;
  internal const int LastFiredTime = 0x15AC;
  internal const int WeaponHash = 0x15DC;
  internal const int AimPunchServices = 0x1598;
  internal const int EyeAngles = 0x35F0;
  internal const int TickBase = 0x6B8;
  internal const int IsLocalController = 0x790;
  internal const int IsValveServer = 0xA4;
  internal const int PredictableTick = 0x48;
  internal const int PredictableTickFraction = 0x4C;
  internal const int PredictableAngle = 0x50;
  internal const int PredictableVelocity = 0x5C;
  internal const int UnpredictableTick = 0xA0;
  internal const int UnpredictableAngle = 0xA4;
 }
}
