using UnityEngine;

namespace WinterMarket
{
    [DisallowMultipleComponent]
    public sealed class NPCFootstepAudioLink : MonoBehaviour
    {
        private NPCDirector director;
        private AmbientController ambient;
        private float nextStepAt;

        public void Configure(NPCDirector npcDirector, AmbientController ambientController)
        {
            director = npcDirector;
            ambient = ambientController;
            nextStepAt = Time.time + 0.8f;
        }

        private void Update()
        {
            if (director == null || ambient == null || director.ActiveCount <= 0 || Time.time < nextStepAt)
            {
                return;
            }

            ambient.PlayFootstep(Mathf.Lerp(0.025f, 0.07f, Mathf.Clamp01(director.ActiveCount / 4f)));
            nextStepAt = Time.time + Random.Range(0.48f, 0.82f);
        }
    }
}
