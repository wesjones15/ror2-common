using System;
using Ror2Common;
using OlimarTest;
static class Program
{
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static void Main()
    {
        var p=new SessionFeaturePolicy();
        Check(p.Reconcile(1,Array.Empty<int>())==1,"solo bonus unavailable");
        Check(p.Reconcile(1,new[]{7})==0,"unreported joining peer enabled bonus");
        p.Report(7,1);Check(p.Reconcile(1,new[]{7})==1,"matching peers disabled bonus");
        p.Report(8,0);Check(p.Reconcile(1,new[]{7,8})==0,"missing-assets peer did not disable bonus");
        Check(p.Reconcile(1,new[]{7})==1,"leaving missing-assets peer did not restore lobby bonus");
        p.Lock();p.Report(7,0);Check(p.Reconcile(0,new[]{7})==1,"active run changed its feature set");
        Check(!p.Compatible(0)&&p.Compatible(1),"late-join compatibility differs from locked set");
        p.Reset();Check(p.Reconcile(0,new[]{7})==0,"previous run leaked features");p.Lock();Check(p.Compatible(0),"base-only session rejected missing assets");
        Check(OnionStandInPolicy.LegsVisible(false)&&!OnionStandInPolicy.LegsVisible(true),"flying Onion still shows legs");
        Check(OnionStandInPolicy.Lift(0)==0&&OnionStandInPolicy.Lift(.7f)>.4f&&OnionStandInPolicy.Lift(2)==1,"captain does not ascend");
        Check(Math.Abs(OnionStandInPolicy.BoardingHeight(.04f)+OnionStandInPolicy.CaptainHeight*.5f-4.4f)<.0001f,"boarding hides the captain below the pod instead of lifting into it");
        Check(OnionStandInPolicy.CaptainVisible(true,0)&&!OnionStandInPolicy.CaptainVisible(true,2)&&OnionStandInPolicy.CaptainVisible(false,2),"boarding/hide/drop visibility");
        Console.WriteLine("PASS: solo/mixed/complete capability sets, lobby join/leave, locked runs, late join, run reset, Onion legs/ascent/hide/drop");
    }
}
