# Interface change revisions

Interface previews compare the effective client text, including the public key derived from the submitted private key. The preview revision of a client is the revision of its exported configuration after applying that request. Server-only settings and overridden fields do not produce a client update.

Interface revision v2 includes the stored settings and excludes creation and modification timestamps. Repeating an existing on/off, default or open-port state does not invalidate a proposal. A real settings change still invalidates it, including administrative flags: applying an old draft must not silently restore those flags.

Drafts prepared before v2 remain visible and can be discarded or explicitly replaced. Their earlier revisions are stale and cannot be applied. Prepare them again before applying; client configurations, interface settings and active exports are not changed by this upgrade.

An existing pending draft cannot be silently replaced through PUT. The caller confirms replacement with the current `expectedDraftRevision`, obtained from the pending response. A concurrent replacement or cancellation rejects that confirmation. Calls without a pending draft retain the immediate application behavior.
