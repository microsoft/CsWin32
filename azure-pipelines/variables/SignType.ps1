if ($env:SYSTEM_COLLECTIONID -eq 'cb55739e-4afe-46a3-970f-1b49d8ee7564') {
    if ($env:BUILD_REASON -eq 'Schedule') {
      'real'
    } else {
      if ($env:SIGNTYPESELECTION) {
        $env:SIGNTYPESELECTION
      } else {
        'test'
      }
    }
  }
