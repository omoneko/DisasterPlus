using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// シーン上の UI コンポーネントを探す。main スレッド専用。
    ///
    /// Object.FindObjectOfType は使わない。Unity 5.6 の同 API は
    /// <b>非アクティブな GameObject 上のコンポーネントを返さない</b>。CS の各パネルは
    /// 「作られてはいるがまだ非アクティブ」という状態を普通に取るので、その瞬間に
    /// 探しに行った MOD は「まだ無い」と判断し、以後アクティブになっても再取得できる保証がない
    /// （最悪、その都市では機能が無言で入らない）。
    ///
    /// Resources.FindObjectsOfTypeAll&lt;T&gt;() は非アクティブも返す（Unity 5.6 に
    /// ジェネリック版が存在することをアセンブリのリフレクションで確認済み）。
    /// ただしシーンに属さないプレハブやアセットも一緒に返ってくるので、
    /// gameObject.scene.IsValid() でシーン上の実体だけに絞る
    /// （GameObject.scene / Scene.IsValid も同じく実在を確認済み）。
    /// </summary>
    public static class SceneObjects
    {
        /// <summary>シーン上の T を 1 つ返す。見つからなければ null。</summary>
        public static T FindInScene<T>() where T : Component
        {
            var all = Resources.FindObjectsOfTypeAll<T>();
            if (all == null) return null;

            for (int i = 0; i < all.Length; i++)
            {
                T c = all[i];
                if (c == null) continue;   // Unity のフェイク null（破棄済み）も弾く

                GameObject go = c.gameObject;
                if (go == null) continue;
                if (!go.scene.IsValid()) continue;   // プレハブ / アセット由来

                return c;
            }
            return null;
        }
    }
}
