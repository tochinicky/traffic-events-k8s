// Build → test → image → deploy to the local kind cluster → smoke test.
// Runs on the Jenkins in ci/jenkins (docker-compose), which shares the host's Docker and sits on
// the `kind` Docker network, so it can reach the cluster's API server and NodePorts by name.
pipeline {
  agent any

  options {
    timestamps()
    timeout(time: 45, unit: 'MINUTES')
    disableConcurrentBuilds() // one deploy to the cluster at a time
    buildDiscarder(logRotator(numToKeepStr: '20'))
  }

  environment {
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_NOLOGO = '1'
    // Shares a small Docker VM with the kind cluster: one MSBuild node, no lingering worker processes.
    MSBUILDDISABLENODEREUSE = '1'
    CLUSTER = 'traffic-events'
    KUBECONFIG = "${WORKSPACE}/.kubeconfig"
    // From inside the kind network, the node container's name replaces localhost.
    INGEST_URL = 'http://traffic-events-control-plane:30080'
    QUERY_URL = 'http://traffic-events-control-plane:30081'
    RESULTS = "${WORKSPACE}/test-results"
  }

  stages {
    stage('Checkout') {
      steps {
        checkout scm
        script {
          // Unique, traceable tag per build: commit + build number.
          env.TAG = sh(script: 'git rev-parse --short HEAD', returnStdout: true).trim() + "-b${env.BUILD_NUMBER}"
        }
        echo "Image tag: ${env.TAG}"
      }
    }

    stage('Restore & Build') {
      steps {
        sh 'dotnet build -c Release -m:1' // analyzers on, warnings are errors
      }
    }

    stage('Unit tests') {
      steps {
        sh 'dotnet test tests/TrafficEvents.UnitTests -c Release --no-build --logger "junit;LogFilePath=$RESULTS/unit.xml"'
      }
    }

    stage('Integration tests') {
      steps {
        // Testcontainers starts RabbitMQ and MongoDB as sibling containers on the host Docker.
        sh 'dotnet test tests/TrafficEvents.IntegrationTests -c Release --no-build --logger "junit;LogFilePath=$RESULTS/integration.xml"'
      }
    }

    stage('Build images') {
      steps {
        sh 'scripts/build-images.sh "$TAG"'
      }
    }

    stage('Load images into kind') {
      steps {
        sh 'scripts/load-images.sh "$TAG"'
      }
    }

    stage('Deploy') {
      steps {
        // secrets.env is a Jenkins file credential (created by configuration-as-code), never in git.
        withCredentials([file(credentialsId: 'traffic-events-secrets-env', variable: 'SECRETS_ENV')]) {
          sh '''
            cp "$SECRETS_ENV" k8s/overlays/dev/secrets.env
            kind get kubeconfig --internal --name "$CLUSTER" > "$KUBECONFIG"
            SKIP_LOAD=1 scripts/deploy.sh "$TAG"
          '''
        }
      }
    }

    stage('Smoke test') {
      steps {
        sh 'SMOKE_TEST_ARGS="--logger junit;LogFilePath=$RESULTS/smoke.xml" scripts/smoke.sh'
      }
    }
  }

  post {
    always {
      junit allowEmptyResults: true, testResults: 'test-results/*.xml'
      sh '''
        if [ -s "$KUBECONFIG" ]; then
          kubectl -n traffic-events get deploy,pods,hpa -o wide > deployment-report.txt 2>&1 || true
        fi
      '''
      archiveArtifacts allowEmptyArchive: true, artifacts: 'deployment-report.txt'
      sh 'rm -f k8s/overlays/dev/secrets.env "$KUBECONFIG"'
    }
    success {
      echo "Deployed ${env.TAG} to kind cluster ${env.CLUSTER}."
    }
    failure {
      echo 'Pipeline failed; see the stage log and test results above.'
    }
  }
}
