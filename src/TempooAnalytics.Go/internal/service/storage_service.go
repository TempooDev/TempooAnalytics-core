package service

import (
	"context"
	"io"
	"time"

	"github.com/aws/aws-sdk-go-v2/aws"
	s3config "github.com/aws/aws-sdk-go-v2/config"
	"github.com/aws/aws-sdk-go-v2/credentials"
	"github.com/aws/aws-sdk-go-v2/service/s3"
)

type StorageService interface {
	UploadFile(ctx context.Context, key string, content io.Reader, contentType string) error
	DownloadFile(ctx context.Context, key string) (io.ReadCloser, error)
	GeneratePresignedURL(ctx context.Context, key string, expiration time.Duration) (string, error)
	DeleteFile(ctx context.Context, key string) error
}

type r2StorageService struct {
	client     *s3.Client
	bucketName string
}

func NewR2StorageService(ctx context.Context, endpoint, accessKey, secretKey, bucket string) (StorageService, error) {
	cfg, err := s3config.LoadDefaultConfig(ctx,
		s3config.WithRegion("auto"),
		s3config.WithCredentialsProvider(credentials.NewStaticCredentialsProvider(accessKey, secretKey, "")),
	)
	if err != nil {
		return nil, err
	}

	client := s3.NewFromConfig(cfg, func(o *s3.Options) {
		o.BaseEndpoint = aws.String(endpoint)
		// R2 and the local MinIO both accept path-style requests; MinIO doesn't resolve bucket subdomains.
		o.UsePathStyle = true
	})

	return &r2StorageService{
		client:     client,
		bucketName: bucket,
	}, nil
}

func (s *r2StorageService) UploadFile(ctx context.Context, key string, content io.Reader, contentType string) error {
	_, err := s.client.PutObject(ctx, &s3.PutObjectInput{
		Bucket:      aws.String(s.bucketName),
		Key:         aws.String(key),
		Body:        content,
		ContentType: aws.String(contentType),
	})
	return err
}

func (s *r2StorageService) DownloadFile(ctx context.Context, key string) (io.ReadCloser, error) {
	output, err := s.client.GetObject(ctx, &s3.GetObjectInput{
		Bucket: aws.String(s.bucketName),
		Key:    aws.String(key),
	})
	if err != nil {
		return nil, err
	}
	return output.Body, nil
}

func (s *r2StorageService) GeneratePresignedURL(ctx context.Context, key string, expiration time.Duration) (string, error) {
	presignClient := s3.NewPresignClient(s.client)
	presignedUrl, err := presignClient.PresignGetObject(ctx, &s3.GetObjectInput{
		Bucket: aws.String(s.bucketName),
		Key:    aws.String(key),
	}, s3.WithPresignExpires(expiration))

	if err != nil {
		return "", err
	}
	return presignedUrl.URL, nil
}

func (s *r2StorageService) DeleteFile(ctx context.Context, key string) error {
	_, err := s.client.DeleteObject(ctx, &s3.DeleteObjectInput{
		Bucket: aws.String(s.bucketName),
		Key:    aws.String(key),
	})
	return err
}
