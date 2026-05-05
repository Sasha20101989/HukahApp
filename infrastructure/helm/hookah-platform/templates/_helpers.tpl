{{- define "hookah-platform.name" -}}
{{ .Chart.Name }}
{{- end }}

{{- define "hookah-platform.fullname" -}}
{{ .Release.Name }}-{{ .Chart.Name }}
{{- end }}
