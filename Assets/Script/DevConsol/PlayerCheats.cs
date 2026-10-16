using Fusion;
using UnityEngine;

/// <summary>
/// 치트를 '남'에게 걸 때 쓰는 통로 — 플레이어 프리팹(과 더미 프리팹)에 붙임.
/// 명령은 그 대상의 권한자(SA)에게 보내져 거기서 실행되고, 결과 글은 보낸 사람 콘솔로 돌아옴.
/// 나 자신에게 거는 치트는 이게 없어도 됨.
/// </summary>
public class PlayerCheats : NetworkBehaviour
{
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Exec(string line, PlayerRef from)
    {
        string result = DevCommands.ExecuteOnSelf(gameObject, line);
        if (from == Runner.LocalPlayer) DevConsole.Print(result);
        else RPC_Reply(from, result);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_Reply([RpcTarget] PlayerRef to, string msg) => DevConsole.Print(msg);
}
