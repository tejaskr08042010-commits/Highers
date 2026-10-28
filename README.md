**Authorship ranking [For C# code]:**

Each script gets a rank for how much of its C# was written by me, searched for on the web, or written by AI. Like a game tier list: S is the top. The ranks describe only the C# code. Models, visuals, state machines, particle effects, and level design are separate and are mine. Tier B, C and F are AI prompts.

- **[Rank]** : [What it represents]
- **S** : I write the code without any assist. AI at most checks for errors. I use this to check if I remember a method after some time. Basically, the syntax I already knew before starting this project (from my earlier projects).
- **A** : I design the mechanism, detailed logic code and structure. AI and web search only fill in the syntax.
- **B** : A faster version of A. I use this when I already know most of the code logic. I design the mechanism and detailed logic. AI creates the code structure and syntax. It's faster than A, so I can be more productive with the time I get after my studies.
- **C** : I design the mechanism and describe in detail what should happen. AI decides the logic and writes all the code.
- **F** : I give a loose instruction and AI generates the code.

*[Illustrative: one simple script, shown at each rank]*

*Example code*

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Activate_Deactivate_OE : MonoBehaviour
{
    [System.Serializable]
    public class TimedObject
    {
        public GameObject targetObject;
        public float delay;
    }

    [Header("Objects to Activate")]
    [Tooltip("Add objects that will be activated after their specified delay")]
    public List<TimedObject> objectsToActivate = new List<TimedObject>();

    [Header("Objects to Deactivate")]
    [Tooltip("Add objects that will be deactivated after their specified delay")]
    public List<TimedObject> objectsToDeactivate = new List<TimedObject>();

    private void OnEnable()
    {
        // Start coroutines for activating objects
        foreach (TimedObject timedObj in objectsToActivate)
        {
            if (timedObj.targetObject != null)
            {
                StartCoroutine(ActivateObjectAfterDelay(timedObj));
            }
        }

        // Start coroutines for deactivating objects
        foreach (TimedObject timedObj in objectsToDeactivate)
        {
            if (timedObj.targetObject != null)
            {
                StartCoroutine(DeactivateObjectAfterDelay(timedObj));
            }
        }
    }

    private IEnumerator ActivateObjectAfterDelay(TimedObject timedObj)
    {
        yield return new WaitForSecondsRealtime(timedObj.delay);
        timedObj.targetObject.SetActive(true);
    }

    private IEnumerator DeactivateObjectAfterDelay(TimedObject timedObj)
    {
        yield return new WaitForSecondsRealtime(timedObj.delay);
        timedObj.targetObject.SetActive(false);
    }

}
```
- **S** : Just wrote it myself.
- **A** : Started writing it myself, asking AI questions like "How can I add floats to each element" and "How to wait for Delay seconds without using Update."
- **B** : "Create a script for Unity 6. Add two element lists with Headers 'Activate' and 'Deactivate.' Each element of both the lists should have a public float 'Delay' and a GameObject slot, just use a separate class for those. OnEnable, after 'Delay' secs (use a coroutine), Activate (SetActive (true)) or Deactivate (SetActive(false)), based on which list the object in question is present."
- **C** : "Create a script for Unity 6. Two lists with elements where I can attach gameobjects and individual delay floats to activate or deactivate them, based on the lists."
- **F** : "Create a unity 6 script having lists where I can slot gameobjects to activate or deactivate them after a delay."

**Scripts [ranked based on the legend above]**

|**Script Name**|**Rank**|**Details**|
|------------------|---|-------------------------------------------------------------------------|
|FluidDomainCheck|S/A|Most of it was written by me, except the few methods that I couldn't remember.|
|StillWaterReactor|S/A|Most of it was written by me, except the few methods that I couldn't remember.|
|BasicMovement_2|A/B|Most parts were written by search, while others generated with detailed prompts.|
|OperatorAI|A/B|Most parts were written by search, while others generated with detailed prompts.|
|OperatorAI_ML|A/B|Most parts were written by search, while others generated with detailed prompts.|
|MagicCircleHUD|B/A|Some parts were written by search, while most generated with detailed prompts.|
|OperatorAgent|B|Syntax was almost completely searched for using AI, while I connected the attacks and rewards.|

(*The Supporting scripts weren't individually ranked. That folder is only included so the variables and connections aren't ambiguous; it isn't the central part of this portfolio. Almost all of them belong to [B/A].*)

**Honorable mentions [Credits for free stuff]**
- operator_config_fresh (ML configuration file): https://docs.unity3d.com/Packages/com.unity.ml-agents@4.0/manual/Training-Configuration-File.html, assisted by AI chatbot (Claude) to better understand the terms.
- Moon Texture for Skybox creation: https://svs.gsfc.nasa.gov/4720
- Stars (Blender): I followed this 1 minute tutorial to learn the technique for creating stars in Blender, though I edited the setup afterwards (and the rest are all me): https://www.youtube.com/watch?v=d1cu8svVkbE
- Advice for the horizon fade in the same skybox: Claude
  - Note: Check out the skybox(es) in the images included in the media section.
- Some textures used in my game: https://polyhaven.com/textures
- Code generation and editing, as explained above: Claude and Gemini (AI chatbots, free plans)

**A note from the student:** *Though coding is mostly about creating efficient methods that work the way you want them to, while being cheap on the hardware and easy to understand (unless it's for a security system I guess), knowing the syntax lets you use the best tools available. So, I've created this table as the SlideRoom prompt said to explain the use of any assistive tool(s) in detail, and I think web searches and AI clearly fit that criterion. Unfortunately due to my family and academics, I couldn't learn the syntax for every method I created, but still have managed to create some scripts I think are original. I've tried my best to make my portfolio as simple as possible to read, while keeping it honest. The goal isn't to show why I should get in, but to let you (the SlideRoom reviewers) decide if I'll be able to thrive at MIT.*

*On a different note, this presentation is itself an A :)*
